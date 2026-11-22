namespace TORTools.Core.SpritePack.Tpac;

/// <summary>Type GUIDs the Bannerlord engine recognises in a TPAC package.</summary>
/// <remarks>
/// Cross-referenced against:
/// - szszss/TpacTool TexturePixelData.cs / Texture.cs (Texture + TEX_PIXELS)
/// - theartur2000/bannerlord-editor-free-sdk sdk/tpac.py:456 (TEX_SETTINGS, TEX_SRCINFO)
/// - direct hex inspection of Modules/TOR_Armory/Assets/GauntletUI/ui_abilityicons_1_tex.tpac
/// </remarks>
public static class TpacGuids
{
    public static readonly Guid Texture       = new("c974cbcb-5f1c-49f6-9a32-2b5b6c92c2e8");
    public static readonly Guid TexSettings   = new("7c475be2-7c8d-431d-b0e1-53218d047fb9");
    public static readonly Guid TexSrcInfo    = new("8ab981a7-6ba0-4908-9606-91ad341d19a9");
    public static readonly Guid TexPixels     = new("70ee4e2c-79e4-4b2d-8d54-d53ecd2a559c");
}
