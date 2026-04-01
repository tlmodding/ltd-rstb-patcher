namespace RSTBPatcher.Core.Calculators;

public class BgymlResourceCalculator : IResourceCalculator
{
    public static uint CalculateSizeOffset(Stream stream, string romfsName)
    {

        if (romfsName.EndsWith(".alto__AltoConfig.bgyml", StringComparison.OrdinalIgnoreCase))
            return 0xB10;

        var extensionOffset = romfsName.IndexOf('.');
        var fullExtension = extensionOffset != -1 ? romfsName[(extensionOffset + 1)..] : string.Empty;


        return fullExtension switch
        {
            "ui__SystemParam.bgyml" => 0x1568,
            "ui__SnapshotCamera.bgyml" => 0x360,
            "ui__Parts3DLayoutParam.bgyml" => 0x400,
            "ui__MessageSystem.bgyml" => 0x400,
            "sound__VoicePlayParam.bgyml" => 0x1CD8,
            "sound__VoiceLanguageOffset.bgyml" => 0x708,
            "sound__OutputDeviceSetting.bgyml" => 0x388,
            "sound__LeakOutSetting.bgyml" => 0x730,
            "sound__LeakOutParam.bgyml" => 0x400,
            "sound__IgnoreDuckingSetting.bgyml" => 992,
            "sound__FaderDuckingParam.bgyml" => 1248,
            "sound__ChimeSetting.bgyml" => 2080,
            "sound__CameraLinkSetting.bgyml" => 968,
            "sound__AIBgmCtrlParam.bgyml" => 864,
            "pp__CombinationDataTableData.bgyml" => 13960,
            "phive__RigidBodyEntityParam.bgyml" => 1112,
            "phive__RigidBodyControllerEntityParam.bgyml" => 1024,
            "gfx__OceanSystemParam.bgyml" => 0x570,

            "actor__ActorColorVariationSetting.bgyml" => 0x360,
            "actor__AccidentSystemParam.bgyml" => 0x360,
            _ => 0x120,
        };
    }
}