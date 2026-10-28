using Volo.Abp.Settings;

namespace PurchaseFlow.Settings;

public class PurchaseFlowSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        //Define your own settings here. Example:
        //context.Add(new SettingDefinition(PurchaseFlowSettings.MySetting1));
    }
}

