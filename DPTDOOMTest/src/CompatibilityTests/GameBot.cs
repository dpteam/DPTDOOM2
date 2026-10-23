using DPTDOOM;

namespace DPTDOOMTest.CompatibilityTests
{
    /// <summary>
    /// Builds a bot exactly the way the game builds one.
    ///
    /// This exists because the tests kept drifting from the product and the drift
    /// cost real time. The game constructs a bot with a brain, a temperament
    /// derived from the map and the player slot, and a danger memory; a test that
    /// writes `new DoomBot(world, player, nav, brain, style)` gets the neutral
    /// temperament instead, which is a bot the game never runs.
    ///
    /// The difference is not academic: when the navigation graph learned about
    /// walk-over doors, all three bot tests failed on MAP01 while the shipped bot
    /// still finished it in 1749 tics. The gap was the construction, not the bots,
    /// and a test that measures the wrong bot is worse than no test - it reports
    /// failures nobody can reproduce.
    ///
    /// The map key mirrors Doom.CurrentMapKey: the level name plus a fingerprint of
    /// its geometry. If these ever disagree, the temperament seed changes and the
    /// test measures a different bot again - so the key is reproduced here rather
    /// than simplified to "MAP01".
    /// </summary>
    internal static class GameBot
    {
        public static DoomBot Create(
            World world,
            Player player,
            NavGraph nav,
            BotBrain brain,
            BotStyle style,
            int slot)
        {
            var key = MapKey(world);

            return new DoomBot(
                world,
                player,
                nav,
                brain,
                style,
                BotTemperament.FromSeed(BotTemperament.SeedFor(key, slot)),
                new BotDanger(world.Map.Sectors.Length));
        }

        public static string MapKey(World world)
        {
            var options = world.Options;

            var name = options.GameMode == GameMode.Commercial
                ? "MAP" + options.Map.ToString("00")
                : "E" + options.Episode + "M" + options.Map;

            var map = world.Map;

            long fingerprint = map.Sectors.Length * 1000003L +
                               map.Lines.Length * 31L +
                               map.Vertices.Length;

            if (map.Vertices.Length > 0)
            {
                fingerprint = fingerprint * 31 + map.Vertices[0].X.Data;
                fingerprint = fingerprint * 31 + map.Vertices[map.Vertices.Length - 1].Y.Data;
            }

            return name + "-" + (fingerprint & 0xFFFFFF).ToString("x6");
        }
    }
}
