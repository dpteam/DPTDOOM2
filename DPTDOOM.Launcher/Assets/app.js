// DPT.DOOM launcher front end.
//
// Runs inside the MSHTML (Trident) control that ships with Windows, so this
// file is deliberately ES5: no arrow functions, no template literals, no
// Promise, no CSS custom properties.
//
// Host bridge (no COM, works from Windows XP up):
//   js  -> host : store the payload, then navigate to "dptdoom://<verb>".
//                 The host cancels that navigation and reads the payload back
//                 with dptdoomTakePayload().
//   host -> js  : dptReceive(verb, payload)
//
// Payload layout (newline separated), shared with LauncherState.Encode:
//   0 executable, 1 iwad, 2 map, 3 skill, 4 address, 5 port, 6 name,
//   7 flag bits, 8 mode, 9 pwads (joined with \u001f), 10 profile slot

'use strict';

var DPT = (function () {

    var $ = function (id) { return document.getElementById(id); };

    var FIELDS = ['executable', 'iwad', 'map', 'skill', 'address', 'port', 'name'];
    var CHECKS = ['nomonsters', 'fast', 'respawn', 'deathmatch'];

    // These three are inverted on purpose: the checkbox says "проходят сквозь
    // друга", and the absence of that is the Doom default. Stored as bits 16, 32
    // and 64 of the same flags field, leaving the classic four bits untouched so
    // an older settings file still decodes to the same game.
    var COOP_CHECKS = ['noblock', 'coopweapons', 'nosharedkeys'];
    var LIST_SEPARATOR = '\u001f';
    var MAX_MODS = 16;

    var mods = [];
    var mode = 0;
    var slot = 1;
    var timer = null;
    var running = false;

    // Last listing received from the host, so the filter can re-render instantly
    // instead of triggering another master query.
    var lastServers = '';
    var hostAvailable = false;

    // -------------------------------------------------------------
    // host bridge
    // -------------------------------------------------------------

    function post(verb, payload) {
        try {
            window.__dptOutbox = payload === undefined ? '' : String(payload);
            window.location.href = 'dptdoom://' + verb;
            return true;
        } catch (e) {
            return false;
        }
    }

    // Called by the host right after it cancels the dptdoom:// navigation.
    window.dptdoomTakePayload = function () {
        var payload = window.__dptOutbox || '';
        window.__dptOutbox = '';
        return payload;
    };

    function collect() {
        var parts = [];
        var i;

        for (i = 0; i < FIELDS.length; i++) {
            parts.push($(FIELDS[i]).value);
        }

        var flags = 0;
        for (i = 0; i < CHECKS.length; i++) {
            if ($(CHECKS[i]).checked) {
                flags |= (1 << i);
            }
        }

        for (i = 0; i < COOP_CHECKS.length; i++) {
            if ($(COOP_CHECKS[i]).checked) {
                flags |= (16 << i);
            }
        }

        parts.push(String(flags));
        parts.push(String(mode));
        parts.push(mods.join(LIST_SEPARATOR));
        parts.push(String(slot));
        parts.push(String(parseInt($('coopbots').value, 10) || 3));
        parts.push($('masterEndpoint').value);
        parts.push($('relaySession').value);
        parts.push($('relay').checked ? '1' : '0');
        parts.push(String(parseInt($('players').value, 10) || 2));
        parts.push(String(parseInt($('hostbots').value, 10) || 0));

        return parts.join('\n');
    }

    function apply(payload) {
        if (!payload) {
            return;
        }

        var parts = String(payload).split('\n');
        var i;

        for (i = 0; i < FIELDS.length; i++) {
            if (i < parts.length) {
                $(FIELDS[i]).value = parts[i];
            }
        }

        var flags = parseInt(parts[FIELDS.length], 10) || 0;
        for (i = 0; i < CHECKS.length; i++) {
            $(CHECKS[i]).checked = (flags & (1 << i)) !== 0;
        }

        for (i = 0; i < COOP_CHECKS.length; i++) {
            $(COOP_CHECKS[i]).checked = (flags & (16 << i)) !== 0;
        }

        mode = parseInt(parts[FIELDS.length + 1], 10) || 0;

        var list = parts[FIELDS.length + 2] || '';
        mods = list.length > 0 ? list.split(LIST_SEPARATOR) : [];

        var newSlot = parseInt(parts[FIELDS.length + 3], 10) || 1;
        slot = newSlot;
        $('slot').value = String(newSlot);

        var bots = parseInt(parts[FIELDS.length + 4], 10) || 3;
        $('coopbots').value = String(bots);

        // An older state has nothing at these offsets; keep the field defaults
        // in that case rather than blanking the master address.
        if (typeof parts[FIELDS.length + 5] === 'string' && parts[FIELDS.length + 5].length > 0) {
            $('masterEndpoint').value = parts[FIELDS.length + 5];
        }

        $('relaySession').value = parts[FIELDS.length + 6] || '';
        $('relay').checked = (parts[FIELDS.length + 7] || '0') === '1';

        var players = parseInt(parts[FIELDS.length + 8], 10);
        if (players >= 2 && players <= 4) {
            $('players').value = String(players);
        }

        var hostBots = parseInt(parts[FIELDS.length + 9], 10);
        if (hostBots >= 0 && hostBots <= 3) {
            $('hostbots').value = String(hostBots);
        }

        syncMode();
        renderMods();
        requestPreview();
    }

    // -------------------------------------------------------------
    // ui state
    // -------------------------------------------------------------

    function syncMode() {
        var buttons = document.querySelectorAll('[data-mode]');
        var i;

        for (i = 0; i < buttons.length; i++) {
            var value = parseInt(buttons[i].getAttribute('data-mode'), 10);
            buttons[i].className = value === mode ? 'selected' : '';
            buttons[i].setAttribute('aria-pressed', value === mode ? 'true' : 'false');
        }

        $('network').className = (mode === 1 || mode === 2) ? '' : 'hidden';
        $('botOptions').className = mode === 3 ? '' : 'hidden';
        $('address').disabled = mode !== 2;
        $('relay').disabled = mode !== 1;
        $('relaySession').disabled = mode !== 2;
        $('players').disabled = mode !== 1;
        $('hostbots').disabled = mode !== 1;
    }

    function renderMods() {
        var list = $('mods');
        var i;

        while (list.firstChild) {
            list.removeChild(list.firstChild);
        }

        for (i = 0; i < mods.length; i++) {
            list.appendChild(modRow(mods[i], i));
        }

        $('empty').className = mods.length > 0 ? 'empty hidden' : 'empty';
        $('modCount').innerHTML = mods.length + ' из ' + MAX_MODS;
        $('addMod').disabled = mods.length >= MAX_MODS;
    }

    function modRow(path, index) {
        var item = document.createElement('li');
        item.className = 'mod';

        var label = document.createElement('span');
        label.title = path;
        label.appendChild(document.createTextNode(
            (index + 1 < 10 ? '0' : '') + (index + 1) + '  ' + path));
        item.appendChild(label);

        item.appendChild(modButton('\u00d7', 'Удалить', index, 0));
        item.appendChild(modButton('\u2193', 'Ниже', index, 1));
        item.appendChild(modButton('\u2191', 'Выше', index, -1));

        return item;
    }

    function modButton(text, title, index, delta) {
        var button = document.createElement('button');
        button.type = 'button';
        button.title = title;
        button.appendChild(document.createTextNode(text));

        button.disabled = delta !== 0 &&
            (index + delta < 0 || index + delta >= mods.length);

        button.onclick = function () {
            if (delta === 0) {
                mods.splice(index, 1);
            } else {
                var moved = mods[index];
                mods[index] = mods[index + delta];
                mods[index + delta] = moved;
            }

            renderMods();
            requestPreview();
        };

        return button;
    }

    function setStatus(text, isError) {
        var node = $('status');
        node.className = isError ? 'status error' : 'status';
        node.innerHTML = '';
        node.appendChild(document.createTextNode(text || ''));
    }

    function setRunning(value) {
        running = value;
        $('launch').disabled = value;
        $('launch').innerHTML = value
            ? 'Игра запущена &nbsp;&#8599;'
            : 'Запустить игру &nbsp;&#8599;';
    }

    function requestPreview() {
        if (timer) {
            clearTimeout(timer);
        }

        timer = setTimeout(function () {
            timer = null;
            post('preview', collect());
        }, 150);
    }

    // -------------------------------------------------------------
    // server browser
    // -------------------------------------------------------------

    function renderServers(payload) {
        // Kept so the filter can re-render without asking the master again: the
        // listing is the same data, only the view changes.
        if (payload !== undefined) {
            lastServers = payload;
        }

        payload = lastServers;

        var list = $('serverList');

        while (list.firstChild) {
            list.removeChild(list.firstChild);
        }

        var rows = payload ? String(payload).split('\n') : [];
        var filter = ($('serverFilter').value || '').toLowerCase();
        var parsed = [];
        var i;

        for (i = 0; i < rows.length; i++) {
            var f = rows[i].split('\t');
            if (f.length < 6) {
                continue;
            }

            // Filter on what a player actually types: the session name, or the
            // map because that is the other thing people look for.
            if (filter.length > 0 &&
                f[0].toLowerCase().indexOf(filter) < 0 &&
                f[3].toLowerCase().indexOf(filter) < 0) {
                continue;
            }

            parsed.push(f);
        }

        if (parsed.length === 0) {
            list.appendChild(document.createTextNode(
                rows.length > 0
                    ? 'Ничего не подходит под фильтр.'
                    : 'Нет объявлений. Хост мог ещё не опубликоваться или уже начать игру.'));
            return;
        }

        // Nearest first: an unreachable server (ping -1) sorts last, because the
        // only thing worse than a distant host is one that cannot be reached.
        parsed.sort(function (a, b) {
            var pa = parseInt(a[8], 10);
            var pb = parseInt(b[8], 10);

            if (pa < 0) { pa = 999999; }
            if (pb < 0) { pb = 999999; }

            return pa - pb;
        });

        for (i = 0; i < parsed.length; i++) {
            list.appendChild(serverCard(parsed[i]));
        }
    }

    function serverCard(f) {
        var card = document.createElement('div');
        card.className = 'server-card';

        var title = document.createElement('strong');
        title.appendChild(document.createTextNode(f[0]));
        card.appendChild(title);

        var session = f.length > 6 ? f[6] : '';
        var relay = f.length > 7 && f[7] === '1';

        // The probe is a TCP handshake to the game port, so it measures the same
        // round trip a tic will take. "--" means the port did not answer at all,
        // which is information too: the listing is worth nothing if nobody can
        // reach the host.
        var ping = f.length > 8 ? parseInt(f[8], 10) : -1;
        var pingText = ping > 0 ? ping + ' ms' : '-- нет ответа';

        var detail = document.createElement('p');
        detail.appendChild(document.createTextNode(
            f[1] + ':' + f[2] + ' · ' + f[3] + ' · skill ' + f[4] +
            ' · ' + pingText + ' · TTL ' + f[5] + 's · ' +
            (relay ? 'DIRECT + RELAY' : 'DIRECT') + ' · UNVERIFIED'));
        card.appendChild(detail);

        var address = f[1] + ':' + f[2];
        var pick = document.createElement('button');
        pick.type = 'button';
        pick.appendChild(document.createTextNode('Выбрать'));
        pick.onclick = function () {
            $('address').value = address;

            // The relay session is part of the listing, so picking a server
            // fills in everything a guest needs. Leaving it stale would send
            // the next game to the previous host's tunnel.
            $('relaySession').value = relay ? session : '';

            mode = 2;
            syncMode();
            requestPreview();
            setStatus('Адрес выбран: ' + address + (relay ? ' (relay доступен)' : ''), false);
        };

        card.appendChild(pick);
        return card;
    }

    // -------------------------------------------------------------
    // theme
    // -------------------------------------------------------------

    function setTheme(theme) {
        var dark = theme === 'dark';
        document.documentElement.className = dark ? 'dark' : '';
        $('theme-toggle').innerHTML = dark ? '☀ Светлая тема' : '☾ Тёмная тема';
        $('theme-toggle').setAttribute('aria-pressed', dark ? 'true' : 'false');
    }

    // -------------------------------------------------------------
    // host -> page
    // -------------------------------------------------------------

    window.dptReceive = function (verb, payload) {
        window.__dptHostSeen = true;

        switch (verb) {
            case 'state':
                apply(payload);
                break;

            case 'preview':
                $('preview').innerHTML = '';
                $('preview').appendChild(document.createTextNode(payload || '—'));
                break;

            case 'status':
                var lines = String(payload || '').split('\n');
                setStatus(lines[0], lines[1] === '1');
                break;

            case 'running':
                setRunning(String(payload) === '1');
                break;

            case 'hasLog':
                $('log').disabled = String(payload) !== '1';
                break;

            case 'theme':
                setTheme(String(payload));
                break;

            case 'storage':
                $('storage').innerHTML = '';
                $('storage').appendChild(document.createTextNode(String(payload || '')));
                break;

            case 'servers':
                renderServers(payload);
                break;

            case 'mods':
                mods = payload ? String(payload).split(LIST_SEPARATOR) : [];
                renderMods();
                break;
        }
    };

    // -------------------------------------------------------------
    // wiring
    // -------------------------------------------------------------

    function changed() {
        syncMode();
        requestPreview();
    }

    function init() {
        var i;

        for (i = 0; i < FIELDS.length; i++) {
            $(FIELDS[i]).onchange = changed;
            $(FIELDS[i]).onkeyup = changed;
        }

        for (i = 0; i < CHECKS.length; i++) {
            $(CHECKS[i]).onclick = changed;
        }

        for (i = 0; i < COOP_CHECKS.length; i++) {
            $(COOP_CHECKS[i]).onclick = changed;
        }

        // These are not in FIELDS (they are launcher-only, not session fields),
        // so they need wiring by hand: without it editing them changes nothing,
        // because no preview is requested.
        var extras = ['players', 'hostbots'];

        for (i = 0; i < extras.length; i++) {
            $(extras[i]).onchange = changed;
            $(extras[i]).onkeyup = changed;
        }

        // The filter is local to the browser: it re-renders the last listing and
        // never asks the master again.
        $('serverFilter').onkeyup = function () {
            renderServers();
        };
        $('serverFilter').onchange = function () {
            renderServers();
        };

        var buttons = document.querySelectorAll('[data-mode]');
        for (i = 0; i < buttons.length; i++) {
            buttons[i].onclick = (function (button) {
                return function () {
                    mode = parseInt(button.getAttribute('data-mode'), 10);
                    changed();
                };
            })(buttons[i]);
        }

        $('theme-toggle').onclick = function () {
            var dark = document.documentElement.className === 'dark';
            setTheme(dark ? 'light' : 'dark');
            post('themeSave', dark ? 'light' : 'dark');
        };

        $('playNav').onclick = function () {
            $('serverBrowser').className = 'panel hidden';
            $('playNav').className = 'active';
            $('serversNav').className = '';
        };

        $('serversNav').onclick = function () {
            $('serverBrowser').className = 'panel';
            $('serversNav').className = 'active';
            $('playNav').className = '';
        };

        $('browseExe').onclick = function () { post('browse', 'exe'); };
        $('browseIwad').onclick = function () { post('browse', 'iwad'); };
        $('addMod').onclick = function () { post('browse', 'pwad'); };
        $('load').onclick = function () { post('profile', 'load'); };
        $('save').onclick = function () { post('profile', 'save'); };
        $('log').onclick = function () { post('openLog', ''); };

        $('slot').onchange = function () {
            slot = parseInt($('slot').value, 10) || 1;
            requestPreview();
            post('profile', 'select');
        };

        $('masterSave').onclick = function () {
            post('masterSave', $('masterEndpoint').value);
        };

        $('refreshServers').onclick = function () {
            setStatus('Запрос к master…', false);
            post('masterRefresh', $('masterEndpoint').value);
        };

        $('launch').onclick = function () {
            if (running) {
                return;
            }

            setStatus('Запуск…', false);
            post('launch', collect());
        };

        syncMode();
        renderMods();

        post('ready', '');

        // If the page runs in a plain browser there is no host and nothing
        // answers, so the launch button must not pretend to work.
        setTimeout(function () {
            if (!window.__dptHostSeen) {
                setStatus('Предпросмотр дизайна. Запуск игры доступен только внутри EXE.', true);
                $('fatal').className = '';
                $('fatal').appendChild(document.createTextNode(
                    'Внешний браузер не имеет доступа к лаунчеру.'));
                $('launch').disabled = true;
            }
        }, 1500);
    }

    return { init: init };
})();

DPT.init();
