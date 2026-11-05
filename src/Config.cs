using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace GPDuel;

public class DatabaseConfig
{
    // Empty Host = no database (statistics and weapon choice are kept only until the map changes)
    [JsonPropertyName("Host")] public string Host { get; set; } = "";
    [JsonPropertyName("Port")] public uint Port { get; set; } = 3306;
    [JsonPropertyName("User")] public string User { get; set; } = "";
    [JsonPropertyName("Password")] public string Password { get; set; } = "";
    [JsonPropertyName("Database")] public string Database { get; set; } = "";
}

public class WeaponEntry
{
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Weapon")] public string Weapon { get; set; } = "";

    public WeaponEntry() { }
    public WeaponEntry(string name, string weapon) { Name = name; Weapon = weapon; }
}

public class GPDuelConfig : BasePluginConfig
{
    [JsonPropertyName("Database")] public DatabaseConfig Database { get; set; } = new();

    [JsonPropertyName("ChatPrefix")] public string ChatPrefix { get; set; } = "{red}[DUEL]{default}";

    // ---- Random (casual) duels ----
    // Kills to finish a random duel; then everybody gets a new random opponent
    [JsonPropertyName("CasualKillsToWin")] public int CasualKillsToWin { get; set; } = 5;
    // Modes for random duels: normal / awp / pistol / deagle / knife
    [JsonPropertyName("CasualModes")] public List<string> CasualModes { get; set; } = ["normal", "normal", "awp", "pistol"];
    // Show the score in random duels too (false = only "Raqib: name")
    [JsonPropertyName("CasualShowScore")] public bool CasualShowScore { get; set; } = false;
    // Seconds a lone player waits before the pairs are shuffled to give him an opponent (0 = off)
    [JsonPropertyName("SoloMaxWaitSeconds")] public int SoloMaxWaitSeconds { get; set; } = 20;

    // ---- Duels from the menu (!duel) ----
    // Wins needed; 0 = no limit ("Cheksiz")
    [JsonPropertyName("WinOptions")] public List<int> WinOptions { get; set; } = [3, 5, 10, 15, 0];
    // Weapons of the "Qurol tanlash" menu
    [JsonPropertyName("ChallengeWeapons")] public List<WeaponEntry> ChallengeWeapons { get; set; } =
    [
        new("AK-47", "weapon_ak47"),
        new("M4A4", "weapon_m4a1"),
        new("M4A1-S", "weapon_m4a1_silencer"),
        new("AWP", "weapon_awp"),
        new("SSG 08 (Scout)", "weapon_ssg08"),
        new("Desert Eagle", "weapon_deagle"),
        new("USP-S", "weapon_usp_silencer"),
        new("Glock-18", "weapon_glock"),
        new("R8 Revolver", "weapon_revolver"),
        new("MP9", "weapon_mp9"),
        new("Nova", "weapon_nova"),
    ];
    [JsonPropertyName("InviteTimeoutSeconds")] public int InviteTimeoutSeconds { get; set; } = 30;
    [JsonPropertyName("RematchTimeoutSeconds")] public int RematchTimeoutSeconds { get; set; } = 15;
    // Bots can be challenged (for testing)
    [JsonPropertyName("AllowBotChallenge")] public bool AllowBotChallenge { get; set; } = false;

    // ---- Weapon choice of the player (!duel -> Asosiy qurol / To'pponcha) ----
    [JsonPropertyName("PrimaryWeapons")] public List<WeaponEntry> PrimaryWeapons { get; set; } =
    [
        new("AK-47", "weapon_ak47"),
        new("M4A4", "weapon_m4a1"),
        new("M4A1-S", "weapon_m4a1_silencer"),
        new("FAMAS", "weapon_famas"),
        new("Galil AR", "weapon_galilar"),
        new("SG 553", "weapon_sg556"),
        new("AUG", "weapon_aug"),
    ];
    [JsonPropertyName("SecondaryWeapons")] public List<WeaponEntry> SecondaryWeapons { get; set; } =
    [
        new("USP-S", "weapon_usp_silencer"),
        new("P2000", "weapon_hkp2000"),
        new("Glock-18", "weapon_glock"),
        new("Desert Eagle", "weapon_deagle"),
        new("P250", "weapon_p250"),
        new("Tec-9", "weapon_tec9"),
        new("Five-SeveN", "weapon_fiveseven"),
        new("CZ75-Auto", "weapon_cz75a"),
        new("R8 Revolver", "weapon_revolver"),
    ];
    [JsonPropertyName("DefaultPrimary")] public string DefaultPrimary { get; set; } = "weapon_ak47";
    [JsonPropertyName("DefaultSecondary")] public string DefaultSecondary { get; set; } = "weapon_usp_silencer";

    // ---- Round ----
    [JsonPropertyName("GiveArmor")] public bool GiveArmor { get; set; } = true;
    [JsonPropertyName("GiveHelmet")] public bool GiveHelmet { get; set; } = true;
    // Seconds after a kill before the next round of the pair starts
    [JsonPropertyName("RoundDelaySeconds")] public float RoundDelaySeconds { get; set; } = 1.0f;
    // Players change sides of the arena every round
    [JsonPropertyName("SwapSidesEachRound")] public bool SwapSidesEachRound { get; set; } = true;
    // Free arenas are picked at random (false = first free one)
    [JsonPropertyName("RandomizeArena")] public bool RandomizeArena { get; set; } = true;

    [JsonPropertyName("HudEnabled")] public bool HudEnabled { get; set; } = true;
    [JsonPropertyName("AdminFlag")] public string AdminFlag { get; set; } = "@css/root";

    // Executed at every map start (the duel mode needs them)
    [JsonPropertyName("ServerCommands")] public List<string> ServerCommands { get; set; } =
    [
        "mp_teammates_are_enemies 1",
        "mp_friendlyfire 0",
        "mp_respawn_on_death_ct 1",
        "mp_respawn_on_death_t 1",
        "mp_respawnwavetime_ct 0",
        "mp_respawnwavetime_t 0",
        "mp_respawn_immunitytime 0",
        "mp_ignore_round_win_conditions 1",
        "mp_maxrounds 0",
        "mp_timelimit 0",
        "mp_halftime 0",
        "mp_roundtime 60",
        "mp_roundtime_defuse 60",
        "mp_roundtime_hostage 60",
        "mp_freezetime 0",
        "mp_warmuptime 5",
        "mp_warmup_pausetimer 0",
        "mp_buytime 0",
        "mp_buy_anywhere 0",
        "mp_startmoney 0",
        "mp_maxmoney 0",
        "mp_free_armor 0",
        "mp_death_drop_gun 0",
        "mp_death_drop_grenade 0",
        "mp_death_drop_defuser 0",
        "mp_give_player_c4 0",
        "mp_t_default_primary \"\"",
        "mp_ct_default_primary \"\"",
        "mp_t_default_secondary \"\"",
        "mp_ct_default_secondary \"\"",
        "mp_t_default_grenades \"\"",
        "mp_ct_default_grenades \"\"",
        "mp_weapons_allow_map_placed 0",
        "mp_autoteambalance 0",
        "mp_limitteams 0",
        "mp_autokick 0",
        "mp_team_intro_time 0",
        "mp_endmatch_votenextmap 0",
        "mp_match_end_changelevel 0",
        "mp_match_end_restart 0",
        "mp_disable_respawn_times 1",
        "spec_freeze_time 0",
        "spec_freeze_time_lock 0",
        "spec_freeze_deathanim_time 0",
        "sv_infinite_ammo 2",
        "sv_hibernate_when_empty 0",
    ];

    [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 1;
}
