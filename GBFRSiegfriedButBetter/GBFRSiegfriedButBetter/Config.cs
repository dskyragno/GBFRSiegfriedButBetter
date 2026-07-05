using GBFRSiegfriedButBetter.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel;

namespace GBFRSiegfriedButBetter
{
    public class Config : Configurable<Config>
    {
        [DisplayName("Enable Perfect Execution after Link Attack")]
        [Description("Authorizes input for Perfect Execution after Link Attack regardless of combo state.")]
        [DefaultValue(true)]
        public bool EnablePerfectExecutionAfterLinkAttack { get; set; } = true;

        [DisplayName("Enable Draconic Release Damage Cap Bypass")]
        [Description("While Draconic Release is active, all attacks have a Damage Cap of 999,999.")]
        [DefaultValue(true)]
        public bool EnableDraconicReleaseCapBypass { get; set; } = true;
    }

    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
    }
}