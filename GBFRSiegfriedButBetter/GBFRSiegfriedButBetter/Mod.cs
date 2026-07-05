using GBFRSiegfriedButBetter.Template;

using Reloaded.Mod.Interfaces;
using Reloaded.Memory.Interfaces;

using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using System.Diagnostics;

namespace GBFRSiegfriedButBetter;

public class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger;
    private readonly IMod _owner;
    private Config _configuration;
    private readonly IModConfig _modConfig;

    // ---- Feature 1: Right-click AA override ----
    public delegate int ResolveAttackIdFunction(nint @this, int mode, int candidateID, int requestedID);
    public IHook<ResolveAttackIdFunction> HOOK_ResolveAttackId;

    // ---- Feature 2: Draconic Release damage cap bypass ----
    public delegate void ApplyOrRemoveEffectFunction(nint rcx, int mode, int r8d, nint r9);
    public IHook<ApplyOrRemoveEffectFunction> HOOK_ApplyOrRemoveEffect;

    public delegate void ApplyDamageCapFunction(nint attacker, nint hitInstance);
    public IHook<ApplyDamageCapFunction> HOOK_ApplyDamageCap;

    private bool _draconicReleaseActive = false;

    private const uint SIEGFRIED_CHAR_ID = 0x11102;
    private const uint DRACONIC_RELEASE_SKILL_ID = 0x6A4;
    private const uint UNCAPPED_DAMAGE_VALUE = 999999;

    private nint _lastKnownPlayerEntity = 0;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _owner = context.Owner;
        _configuration = context.Configuration;
        _modConfig = context.ModConfig;

#if DEBUG
        Debugger.Launch();
#endif

        WeakReference<IStartupScanner> startupScanner = _modLoader.GetController<IStartupScanner>();
        if (!startupScanner.TryGetTarget(out IStartupScanner? target))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] Unable to get IStartupScanner?");
            return;
        }

        var imageBase = Process.GetCurrentProcess().MainModule.BaseAddress;

        // Feature 1: combo resolver
        target.AddMainModuleScan("55 41 57 41 56 56 57 53 48 81 EC ?? ?? 00 00", (result) =>
        {
            var addr = imageBase + result.Offset;
            HOOK_ResolveAttackId = _hooks!.CreateHook<ResolveAttackIdFunction>(ResolveAttackId_Hook, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ResolveAttackId at 0x{addr.ToInt64():X}", System.Drawing.Color.LimeGreen);
        });

        // Feature 2a: status effect apply/remove
        target.AddMainModuleScan("55 41 57 41 56 41 55 41 54 56 57 53 48 81 EC ?? 00 00 00 48 8D AC 24 ?? 00 00 00 48 83 E4 E0 48 89 E3 48 89 AB ?? 00 00 00", (result) =>
        {
            var addr = imageBase + result.Offset;
            HOOK_ApplyOrRemoveEffect = _hooks!.CreateHook<ApplyOrRemoveEffectFunction>(ApplyOrRemoveEffect_Hook, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ApplyOrRemoveEffect at 0x{addr.ToInt64():X}", System.Drawing.Color.LimeGreen);
        });

        // Feature 2b: damage cap function
        target.AddMainModuleScan("41 57 41 56 41 55 41 54 56 57 55 53 48 81 EC ?? ?? 00 00", (result) =>
        {
            var addr = imageBase + result.Offset;
            HOOK_ApplyDamageCap = _hooks!.CreateHook<ApplyDamageCapFunction>(ApplyDamageCap_Hook, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ApplyDamageCap at 0x{addr.ToInt64():X}", System.Drawing.Color.LimeGreen);
        });
    }

    // ---- Feature 1 ----
    private int ResolveAttackId_Hook(nint @this, int mode, int candidateID, int requestedID)
    {
        if (@this != 0)
            _lastKnownPlayerEntity = @this;
            
        int result = HOOK_ResolveAttackId.OriginalFunction(@this, mode, candidateID, requestedID);

        if (!_configuration.EnablePerfectExecutionAfterLinkAttack || @this == 0)
            return result;

        try
        {
            uint rightClickFlag = Reloaded.Memory.Memory.Instance.Read<uint>((nuint)(@this + 0x264C));
            if (rightClickFlag != 1)
                return result;

            nint subObj = Reloaded.Memory.Memory.Instance.Read<nint>((nuint)(@this + 0x76B0));
            if (subObj == 0)
                return result;

            uint linkState = Reloaded.Memory.Memory.Instance.Read<uint>((nuint)(subObj + 0x40));
            if (linkState == 0x2B)
                return 0xAA;
        }
        catch { }

        return result;
    }

    // ---- Feature 2a ----
    private void ApplyOrRemoveEffect_Hook(nint rcx, int mode, int r8d, nint r9)
    {
        HOOK_ApplyOrRemoveEffect.OriginalFunction(rcx, mode, r8d, r9);

        if (!_configuration.EnableDraconicReleaseCapBypass || rcx == 0)
            return;

        try
        {
            uint charId = Reloaded.Memory.Memory.Instance.Read<uint>((nuint)(rcx + 0x58));
            uint skillId = Reloaded.Memory.Memory.Instance.Read<uint>((nuint)(rcx + 0x60));

            if (charId == SIEGFRIED_CHAR_ID && skillId == DRACONIC_RELEASE_SKILL_ID)
            {
                _draconicReleaseActive = (mode == 0);
                _logger.WriteLine($"[{_modConfig.ModId}] Draconic Release active = {_draconicReleaseActive}");
            }
        }
        catch { }
    }

    // ---- Feature 2b ----
    private void ApplyDamageCap_Hook(nint attacker, nint hitInstance)
    {
        HOOK_ApplyDamageCap.OriginalFunction(attacker, hitInstance);

        if (!_configuration.EnableDraconicReleaseCapBypass || !_draconicReleaseActive || hitInstance == 0)
            return;

        if (attacker != _lastKnownPlayerEntity)
            return;

        try
        {
            Reloaded.Memory.Memory.Instance.Write((nuint)(hitInstance + 0x264), UNCAPPED_DAMAGE_VALUE);
        }
        catch { }
    }

    #region Standard Overrides
    public override void ConfigurationUpdated(Config configuration)
    {
        _configuration = configuration;
        _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");
    }
    #endregion

    #region For Exports, Serialization etc.
#pragma warning disable CS8618
    public Mod() { }
#pragma warning restore CS8618
    #endregion
}