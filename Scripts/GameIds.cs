using System;
using System.Collections.Generic;
using System.Globalization;

namespace HoldfastCommander
{
    /// <summary>
    /// Valeurs entières des enums du jeu, lues dans HoldfastEnums.Runtime.dll du serveur le 31/10/2026 (docs/CARBONPLAYERS.md §0).
    /// Plusieurs enums ont des valeurs explicites avec des trous : ne jamais compter les positions de docs/ref/HoldfastEnums.Runtime.cs.txt.
    /// Les enums des DLL du mod ont les mêmes valeurs mais il leur manque des noms : le mod convertit chaque valeur reçue en int dès
    /// l'événement et ne travaille que sur ces constantes. Les tables Names (valeur → nom) servent au journal et aux commandes
    /// (le jeu lit les noms d'enum sans tenir compte de la casse). À refaire après une mise à jour du jeu qui ajoute des valeurs.
    /// </summary>
    public static class GameIds
    {
        /// <summary>Classes de joueur (byte). Trous : 14, 15, 17.</summary>
        public static class PlayerClass
        {
            public const int None = 0;
            public const int ArmyLineInfantry = 1;
            public const int NavalMarine = 2;
            public const int NavalCaptain = 3;
            public const int NavalSailor = 4;
            public const int NavalSailor2 = 5;
            public const int ArmyInfantryOfficer = 6;
            public const int CoastGuard = 7;
            public const int Carpenter = 8;
            public const int Surgeon = 9;
            public const int Rifleman = 10;
            public const int LightInfantry = 11;
            public const int FlagBearer = 12;
            public const int Customs = 13;
            public const int Guard = 16;
            public const int Grenadier = 18;
            public const int Musician = 19;
            public const int Sergeant = 20;
            public const int Cannoneer = 21;
            public const int Deprecated_Rocketeer = 22;
            public const int Sapper = 23;
            public const int Hussar = 24;
            public const int Dragoon = 25;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "ArmyLineInfantry" }, { 2, "NavalMarine" }, { 3, "NavalCaptain" }, { 4, "NavalSailor" },
                { 5, "NavalSailor2" }, { 6, "ArmyInfantryOfficer" }, { 7, "CoastGuard" }, { 8, "Carpenter" }, { 9, "Surgeon" },
                { 10, "Rifleman" }, { 11, "LightInfantry" }, { 12, "FlagBearer" }, { 13, "Customs" }, { 16, "Guard" },
                { 18, "Grenadier" }, { 19, "Musician" }, { 20, "Sergeant" }, { 21, "Cannoneer" }, { 22, "Deprecated_Rocketeer" },
                { 23, "Sapper" }, { 24, "Hussar" }, { 25, "Dragoon" },
            };
        }

        /// <summary>Factions. Noms 9 à 12 absents côté mod ; Frontlines internes à 100-106.</summary>
        public static class FactionCountry
        {
            public const int None = 0;
            public const int British = 1;
            public const int French = 2;
            public const int Prussian = 3;
            public const int Russian = 4;
            public const int Italian = 5;
            public const int Allied = 6;
            public const int Central = 7;
            public const int Austrian = 8;
            public const int Privateer = 9;
            public const int Spanish = 10;
            public const int ARBritish = 11;
            public const int ARAmerican = 12;
            public const int Internal_Frontline_British = 100;
            public const int Internal_Frontline_French = 101;
            public const int Internal_Frontline_USA = 102;
            public const int Internal_Frontline_Ottoman = 103;
            public const int Internal_Frontline_Austria = 104;
            public const int Internal_Frontline_German = 105;
            public const int Internal_Frontline_Australian = 106;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "British" }, { 2, "French" }, { 3, "Prussian" }, { 4, "Russian" }, { 5, "Italian" },
                { 6, "Allied" }, { 7, "Central" }, { 8, "Austrian" }, { 9, "Privateer" }, { 10, "Spanish" }, { 11, "ARBritish" },
                { 12, "ARAmerican" }, { 100, "Internal_Frontline_British" }, { 101, "Internal_Frontline_French" },
                { 102, "Internal_Frontline_USA" }, { 103, "Internal_Frontline_Ottoman" }, { 104, "Internal_Frontline_Austria" },
                { 105, "Internal_Frontline_German" }, { 106, "Internal_Frontline_Australian" },
            };
        }

        /// <summary>Ordres du menu officier et sergent (sans trou ; 17 à 34 sans nom côté mod).</summary>
        public static class HighCommandOrderType
        {
            public const int None = 0;
            public const int OfficerReinforce = 1;
            public const int OfficerCharge = 2;
            public const int OfficerFormLine = 3;
            public const int OfficerBreakRankAndFollow = 4;
            public const int OfficerFireAtWill = 5;
            public const int OfficerMakeReady = 6;
            public const int OfficerFire = 7;
            public const int OfficerCeaseFire = 8;
            public const int SergeantFormLine = 9;
            public const int SergeantRally = 12;
            public const int Dash = 31;
            public const int SteadyAim = 32;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "OfficerReinforce" }, { 2, "OfficerCharge" }, { 3, "OfficerFormLine" },
                { 4, "OfficerBreakRankAndFollow" }, { 5, "OfficerFireAtWill" }, { 6, "OfficerMakeReady" }, { 7, "OfficerFire" },
                { 8, "OfficerCeaseFire" }, { 9, "SergeantFormLine" }, { 10, "SergeantFire" }, { 11, "SergeantFireAtWill" },
                { 12, "SergeantRally" }, { 13, "SergeantBreakRankAndFollow" }, { 14, "SergeantMakeReady" },
                { 15, "SergeantCeaseFire" }, { 16, "CarpenterBerserk" }, { 17, "OfficerSquadCamp" }, { 18, "Raid" },
                { 19, "CaptainLoadGuns" }, { 20, "CaptainFireGuns" }, { 21, "CaptainBrace" }, { 22, "CaptainBoardingAction" },
                { 23, "CaptainWetSails" }, { 24, "CaptainHoistFlag" }, { 25, "CaptainRepair" }, { 26, "MidshipmanRallyGunCrew" },
                { 27, "MidshipmanImproveShipHandling" }, { 28, "MidshipmanRallyRepairGang" }, { 29, "CaptainFireAtWill" },
                { 30, "CaptainCeaseFire" }, { 31, "Dash" }, { 32, "SteadyAim" }, { 33, "HandGrenade" }, { 34, "SatchelCharge" },
            };
        }

        /// <summary>Phrases vocales utiles au mod. Trous : 14, 28, 32, 91 à 94.</summary>
        public static class CharacterVoicePhrase
        {
            public const int CeaseFire = 7;
            public const int Charge = 8;
            public const int Compliment = 11;
            public const int Fire = 17;
            public const int FireAtWill = 18;
            public const int FirstRankFire = 19;
            public const int FixBayonet = 20;
            public const int GetDown = 23;
            public const int Help = 29;
            public const int MakeReady = 46;
            public const int PresentArms = 54;
            public const int PrimeLoad = 55;
            public const int RemoveBayonet = 63;
            public const int Retreat = 67;
            public const int SecondRankFire = 72;
            public const int ShoulderArms = 75;
            public const int StandGround = 77;
            public const int Surrender = 80;
            public const int TakeAim = 82;
            public const int Warcry = 95;
            public const int PatrioticCheer = 97;
            public const int HurrahCheer = 98;
            public const int FormColumn = 99;
            public const int ForwardMarch = 100;
            public const int Follow = 101;
            public const int Halt = 102;
            public const int AtTheDouble = 103;
            public const int CloseRanks = 104;
            public const int DoubleRank = 105;
            public const int SingleRank = 106;
            public const int Reinforce = 107;
            public const int Scared = 109;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "AbandonShip" }, { 2, "AimHigher" }, { 3, "AimLower" }, { 4, "AimRockets" },
                { 5, "BailShip" }, { 6, "BelayOrder" }, { 7, "CeaseFire" }, { 8, "Charge" }, { 9, "Cheer" },
                { 10, "CloseGunports" }, { 11, "Compliment" }, { 12, "Disguise" }, { 13, "DouseLanterns" }, { 15, "DropAnchor" },
                { 16, "EnemyVesselSighted" }, { 17, "Fire" }, { 18, "FireAtWill" }, { 19, "FirstRankFire" }, { 20, "FixBayonet" },
                { 21, "FullPort" }, { 22, "FullStarboard" }, { 23, "GetDown" }, { 24, "GoodShot" }, { 25, "HalfPort" },
                { 26, "HalfStarboard" }, { 27, "HeaveTheLead" }, { 29, "Help" }, { 30, "HoistColors" }, { 31, "Insult" },
                { 33, "LaunchBoats" }, { 34, "LeftBroadsideFire" }, { 35, "LightLanterns" }, { 36, "LoadCanisterShot" },
                { 37, "LoadCarcassRocket" }, { 38, "LoadCaseRocket" }, { 39, "LoadChainShot" }, { 40, "LoadHotShot" },
                { 41, "LoadRocket" }, { 42, "LoadRoundShot" }, { 43, "LoadShot" }, { 44, "LowerGuns" }, { 45, "LowerRockets" },
                { 46, "MakeReady" }, { 47, "MakeSail" }, { 48, "Medic" }, { 49, "Mutiny" }, { 50, "NoShouts" }, { 51, "NoSir" },
                { 52, "OpenGunports" }, { 53, "PrepareCombat" }, { 54, "PresentArms" }, { 55, "PrimeLoad" },
                { 56, "PutOutFires" }, { 57, "QuarterPort" }, { 58, "QuarterStarboard" }, { 59, "RaiseGuns" },
                { 60, "RaiseRockets" }, { 61, "RamShot" }, { 62, "ReadyGuns" }, { 63, "RemoveBayonet" }, { 64, "RepairBoat" },
                { 65, "RepairShip" }, { 66, "RepelBoarders" }, { 67, "Retreat" }, { 68, "RetrieveBoats" },
                { 69, "RightBroadsideFire" }, { 70, "RunOutGuns" }, { 71, "Salute" }, { 72, "SecondRankFire" },
                { 73, "SectionsFire" }, { 74, "ShortenSail" }, { 75, "ShoulderArms" }, { 76, "SightGuns" }, { 77, "StandGround" },
                { 78, "StayCalm" }, { 79, "StrikeColors" }, { 80, "Surrender" }, { 81, "Swivel" }, { 82, "TakeAim" },
                { 83, "TakeHelm" }, { 84, "TakeHer" }, { 85, "Taunt" }, { 86, "Thanks" }, { 87, "VesselSighted" },
                { 88, "WeighAnchor" }, { 89, "WetSail" }, { 90, "YesSir" }, { 95, "Warcry" }, { 96, "Duel" },
                { 97, "PatrioticCheer" }, { 98, "HurrahCheer" }, { 99, "FormColumn" }, { 100, "ForwardMarch" }, { 101, "Follow" },
                { 102, "Halt" }, { 103, "AtTheDouble" }, { 104, "CloseRanks" }, { 105, "DoubleRank" }, { 106, "SingleRank" },
                { 107, "Reinforce" }, { 108, "Brains" }, { 109, "Scared" },
            };
        }

        /// <summary>Buffs. Trous : 5 et 28.</summary>
        public static class BuffType
        {
            public const int None = 0;
            public const int StandardBearer = 1;
            public const int StandardBearerCompanion = 2;
            public const int Musician = 3;
            public const int MusicianCompanion = 4;
            public const int Guardsman = 6;
            public const int GuardsmanCompanion = 7;
            public const int OfficerInLine = 8;
            public const int OfficerLineCharge = 9;
            public const int OfficerLineFire = 10;
            public const int OfficerLineFireAtWill = 11;
            public const int SergeantInLine = 12;
            public const int SergeantLineRally = 13;
            public const int SergeantLineFire = 14;
            public const int SergeantLineFireAtWill = 15;
            public const int CarpenterBerserk = 16;
            public const int Raid = 17;
            public const int CaptainBoardingAction = 18;
            public const int CaptainFireAtWill = 19;
            public const int CaptainLoadGuns = 20;
            public const int CaptainFireGuns = 21;
            public const int CaptainBrace = 22;
            public const int CaptainWetSails = 23;
            public const int CaptainRepair = 24;
            public const int MidshipmanRally = 25;
            public const int MidshipmanImproveShipHandling = 26;
            public const int MidshipmanRepair = 27;
            public const int Dash = 29;
            public const int SteadyAim = 30;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "StandardBearer" }, { 2, "StandardBearerCompanion" }, { 3, "Musician" },
                { 4, "MusicianCompanion" }, { 6, "Guardsman" }, { 7, "GuardsmanCompanion" }, { 8, "OfficerInLine" },
                { 9, "OfficerLineCharge" }, { 10, "OfficerLineFire" }, { 11, "OfficerLineFireAtWill" }, { 12, "SergeantInLine" },
                { 13, "SergeantLineRally" }, { 14, "SergeantLineFire" }, { 15, "SergeantLineFireAtWill" },
                { 16, "CarpenterBerserk" }, { 17, "Raid" }, { 18, "CaptainBoardingAction" }, { 19, "CaptainFireAtWill" },
                { 20, "CaptainLoadGuns" }, { 21, "CaptainFireGuns" }, { 22, "CaptainBrace" }, { 23, "CaptainWetSails" },
                { 24, "CaptainRepair" }, { 25, "MidshipmanRally" }, { 26, "MidshipmanImproveShipHandling" },
                { 27, "MidshipmanRepair" }, { 29, "Dash" }, { 30, "SteadyAim" },
            };
        }

        /// <summary>Actions de joueur utiles aux bots (sans trou).</summary>
        public static class PlayerActions
        {
            public const int None = 0;
            public const int StartAimingFirearm = 3;
            public const int StopAimingFirearm = 4;
            public const int FireFirearm = 5;
            public const int Run = 8;
            public const int Crouch = 9;
            public const int StartReloadFirearm = 12;
            public const int EnableCombatStance = 15;
            public const int DisableCombatStance = 16;
            public const int MeleeStrikeHigh = 20;
            public const int MeleeStrikeLow = 21;
            public const int MeleeStrikeLeft = 22;
            public const int MeleeStrikeRight = 23;
            public const int StartCrouching = 32;
            public const int StopCrouching = 33;
            public const int ExecuteSecondaryAttack = 48;
            public const int PlayerGestureSurrender = 52;
            public const int StartBayonetAttach = 63;
            public const int StartBayonetDetach = 64;
            public const int PlayerGesturePanic = 69;
            public const int PlayerGestureCharge = 71;
            public const int PlayerGestureCheer = 75;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "Obsolete_ShipAutoMove" }, { 2, "Jump" }, { 3, "StartAimingFirearm" },
                { 4, "StopAimingFirearm" }, { 5, "FireFirearm" }, { 6, "Obsolete_FireCannon" }, { 7, "Obsolete_Driveship" },
                { 8, "Run" }, { 9, "Crouch" }, { 10, "PlayerAutoWalk" }, { 11, "SwitchWeapon" }, { 12, "StartReloadFirearm" },
                { 13, "InterruptReloadFirearm" }, { 14, "FinishReloadFirearm" }, { 15, "EnableCombatStance" },
                { 16, "DisableCombatStance" }, { 17, "StartMeleeWeaponHold" }, { 18, "StopMeleeWeaponHold" },
                { 19, "ExecuteMeleeWeaponStrike" }, { 20, "MeleeStrikeHigh" }, { 21, "MeleeStrikeLow" },
                { 22, "MeleeStrikeLeft" }, { 23, "MeleeStrikeRight" }, { 24, "StartMeleeBlock" }, { 25, "StopMeleeBlock" },
                { 26, "ChangeMeleeBlock" }, { 27, "MeleeBlockHigh" }, { 28, "MeleeBlockLow" }, { 29, "MeleeBlockLeft" },
                { 30, "MeleeBlockRight" }, { 31, "FireDryFirearm" }, { 32, "StartCrouching" }, { 33, "StopCrouching" },
                { 34, "StopGestureAnimation" }, { 35, "PlayerGestureDanceGanGamStyle" }, { 36, "PlayerGestureDanceHipHop" },
                { 37, "PlayerGestureDanceThriller" }, { 38, "PlayerGestureDanceBelly" }, { 39, "PlayerGestureMusketKick" },
                { 40, "PlayerGestureMusketCrouch" }, { 41, "PlayerGestureInstrumentBass" },
                { 42, "PlayerGestureInstrumentGuitar" }, { 43, "PlayerGestureInstrumentDrums" }, { 44, "PlayerGestureSalute" },
                { 45, "PlayerGesturePointing" }, { 46, "PlayerGestureLookingHorizon" }, { 47, "PlayerGestureMaleLayingDownPose" },
                { 48, "ExecuteSecondaryAttack" }, { 49, "PlayerGestureSaluteMusket" }, { 50, "PlayerGesturePresentArmsMusket" },
                { 51, "PlayerGesturePray" }, { 52, "PlayerGestureSurrender" }, { 53, "PlayerGesturePoint" },
                { 54, "PlayerGestureWave" }, { 55, "PlayerGestureSillyDance" }, { 56, "PlayerGestureSaluteSword" },
                { 57, "PlayerGestureAttentionWave" }, { 58, "PlayerGestureDancingTwerk" }, { 59, "PlayerGestureDancingFunny" },
                { 60, "PlayerGestureSwingDancing" }, { 61, "PlayerGestureNorthernSoulFloorCombo" },
                { 62, "PlayerGestureBackFlip" }, { 63, "StartBayonetAttach" }, { 64, "StartBayonetDetach" },
                { 65, "InterruptBayonetAttach" }, { 66, "InterruptBayonetDetach" }, { 67, "FinishBayonetAttach" },
                { 68, "FinishBayonetDetach" }, { 69, "PlayerGesturePanic" }, { 70, "PlayerGestureSitDown" },
                { 71, "PlayerGestureCharge" }, { 72, "PlayerGestureTauntAxe" }, { 73, "PlayerGestureTauntAxe2" },
                { 74, "WeaponUnloaded" }, { 75, "PlayerGestureCheer" }, { 76, "StartGestureAnimation" },
                { 77, "PlayerGestureRPSPaper" }, { 78, "PlayerGestureRPSRock" }, { 79, "PlayerGestureRPSScissors" },
                { 80, "PlayerGestureWhistle" }, { 81, "StartCrouchWalkingWeaponGesture" },
                { 82, "ExitCrouchWalkingWeaponGesture" }, { 83, "PlayerGesturePreach" }, { 84, "PlayerGestureDrunk" },
                { 85, "PlayerGestureJig" }, { 86, "PlayerGestureLookout" }, { 87, "PlayerGestureDrinkTea" },
                { 88, "PlayerGestureRingBell" },
            };
        }

        /// <summary>Instruments (instrumentPlay).</summary>
        public static class InstrumentType
        {
            public const int None = 0;
            public const int Piano = 1;
            public const int Synth = 2;
            public const int MarchingDrum = 3;
            public const int Fife = 4;
            public const int Violin = 5;
            public const int Bagpipes = 6;
            public const int Gramophone = 7;
            public const int Cello = 8;
            public const int Accordion = 9;
            public const int Banjo = 10;
            public const int Bugle = 11;

            public static readonly Dictionary<int, string> Names = new Dictionary<int, string>
            {
                { 0, "None" }, { 1, "Piano" }, { 2, "Synth" }, { 3, "MarchingDrum" }, { 4, "Fife" }, { 5, "Violin" },
                { 6, "Bagpipes" }, { 7, "Gramophone" }, { 8, "Cello" }, { 9, "Accordion" }, { 10, "Banjo" }, { 11, "Bugle" },
            };
        }

        // ---- Noms ----------------------------------------------------------------------------

        /// <summary>Nom d'une valeur, ou « #n » si elle est inconnue (valeur ajoutée par le jeu depuis le relevé).</summary>
        public static string Name(Dictionary<int, string> names, int value)
        {
            return names.TryGetValue(value, out var name) ? name : "#" + value.ToString(CultureInfo.InvariantCulture);
        }

        public static string ClassName(int value) { return Name(PlayerClass.Names, value); }
        public static string FactionName(int value) { return Name(FactionCountry.Names, value); }
        public static string OrderName(int value) { return Name(HighCommandOrderType.Names, value); }
        public static string PhraseName(int value) { return Name(CharacterVoicePhrase.Names, value); }
        public static string BuffName(int value) { return Name(BuffType.Names, value); }
        public static string ActionName(int value) { return Name(PlayerActions.Names, value); }
        public static string InstrumentName(int value) { return Name(InstrumentType.Names, value); }

        // Noms courts acceptés dans le chat (« !ligne 20 garde »), en plus des noms du jeu et des numéros.
        private static readonly Dictionary<string, int> ClassAliases = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "ligne", PlayerClass.ArmyLineInfantry },
            { "line", PlayerClass.ArmyLineInfantry },
            { "garde", PlayerClass.Guard },
            { "guard", PlayerClass.Guard },
            { "grenadier", PlayerClass.Grenadier },
            { "legere", PlayerClass.LightInfantry },
            { "légère", PlayerClass.LightInfantry },
            { "light", PlayerClass.LightInfantry },
            { "carabinier", PlayerClass.Rifleman },
            { "rifleman", PlayerClass.Rifleman },
            { "marine", PlayerClass.NavalMarine },
            { "douanier", PlayerClass.Customs },
            { "garde-cote", PlayerClass.CoastGuard },
            { "drapeau", PlayerClass.FlagBearer },
            { "tambour", PlayerClass.Musician },
            { "musicien", PlayerClass.Musician },
            { "sergent", PlayerClass.Sergeant },
        };

        /// <summary>Classe donnée par son nom du jeu, un alias français ou son numéro.</summary>
        public static bool TryParseClass(string text, out int value)
        {
            value = PlayerClass.None;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (ClassAliases.TryGetValue(text, out value)) return true;
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return value != PlayerClass.None && PlayerClass.Names.ContainsKey(value);
            foreach (var pair in PlayerClass.Names)
            {
                if (pair.Key == PlayerClass.None || !string.Equals(pair.Value, text, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Key;
                return true;
            }
            value = PlayerClass.None;
            return false;
        }

        /// <summary>Officier d'infanterie : seul à pouvoir lever une ligne (NavalCaptain exclu : pas de ligne à bord).</summary>
        public static bool IsInfantryOfficer(int playerClass)
        {
            return playerClass == PlayerClass.ArmyInfantryOfficer;
        }
    }
}
