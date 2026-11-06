using System;

namespace CarMecaSim.Mp.Game
{
    /// <summary>
    /// Economy access via the game's own GlobalData static API. Using SetPlayerMoney/AddPlayerMoney
    /// (instead of writing the field) fires the game's money events, so the native HUD updates too.
    /// </summary>
    public static class GameState
    {
        public static string Status { get; private set; } = "GlobalData (interop)";

        public static bool Available
        {
            get { try { _ = GlobalData.PlayerMoney; return true; } catch (Exception e) { Status = "ex: " + e.Message; return false; } }
        }

        public static int Money
        {
            get { try { return GlobalData.PlayerMoney; } catch { return 0; } }
            set { try { GlobalData.SetPlayerMoney(value); } catch { } }   // fires the money event -> HUD refresh
        }
        public static void AddMoney(int delta) { try { GlobalData.AddPlayerMoney(delta); } catch { } }

        public static int Scraps
        {
            get { try { return GlobalData.PlayerScraps; } catch { return 0; } }
            set { try { GlobalData.SetPlayerScraps(value); } catch { } }
        }
        public static void AddScraps(int delta) { try { GlobalData.AddPlayerScraps(delta); } catch { } }

        public static int Exp
        {
            get { try { return GlobalData.PlayerExp; } catch { return 0; } }
            set { try { GlobalData.PlayerExp = value; } catch { } }
        }
        public static void AddExp(int delta) { try { GlobalData.AddPlayerExp(delta, false); } catch { } }

        public static int Level
        {
            get { try { return GlobalData.PlayerLevel; } catch { return 0; } }
            set { try { GlobalData.PlayerLevel = value; } catch { } }
        }

        public static int Jobs { get { try { return GlobalData.Jobs; } catch { return 0; } } }
    }
}
