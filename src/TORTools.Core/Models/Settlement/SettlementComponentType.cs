namespace TORTools.Core.Models.Settlement;

/// <summary>
/// Types of settlement components in TOR.
/// </summary>
public enum SettlementComponentType
{
    /// <summary>Unknown or unrecognized component type.</summary>
    Unknown,

    // Standard Bannerlord types
    /// <summary>Major settlement with full infrastructure.</summary>
    Town,

    /// <summary>Fortified settlement (Town with is_castle=true).</summary>
    Castle,

    /// <summary>Small settlement bound to a Town or Castle.</summary>
    Village,

    /// <summary>Bandit/outlaw hideout.</summary>
    Hideout,

    // TOR-specific types
    /// <summary>Religious shrine (Sigmar, Taal, Lady, Myrmidia, Grimnir, Grungni, etc.).</summary>
    Shrine,

    /// <summary>Beastmen herdstone.</summary>
    HerdStone,

    /// <summary>Wood Elf Oak of Ages.</summary>
    OakOfAges,

    /// <summary>Wood Elf world roots network.</summary>
    WorldRoots,

    /// <summary>Chaos portal/warpgate.</summary>
    ChaosPortal,

    /// <summary>Slaver camp.</summary>
    SlaverCamp
}
