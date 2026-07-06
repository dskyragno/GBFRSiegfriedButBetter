using GBFRSiegfriedButBetter.Template;

using Reloaded.Mod.Interfaces;
using Reloaded.Memory.Interfaces;

using Reloaded.Hooks.Definitions;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace GBFRSiegfriedButBetter;

public class Mod : ModBase
{
    private readonly IModLoader _modLoader;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger;
    private readonly IMod _owner;
    private Config _configuration;
    private readonly IModConfig _modConfig;

    // ---- Feature 1: Link Attack followup override ----
    public delegate int ResolveAttackIdFunction(nint @this, int mode, int candidateID, int requestedID);
    public IHook<ResolveAttackIdFunction> HOOK_ResolveAttackId = null!;
    private ResolveAttackIdFunction _resolveAttackIdDelegate = null!;

    // ---- Feature 2: Draconic Release damage cap bypass ----
    public delegate void ApplyOrRemoveEffectFunction(nint rcx, int mode, int r8d, nint r9);
    public IHook<ApplyOrRemoveEffectFunction> HOOK_ApplyOrRemoveEffect = null!;
    private ApplyOrRemoveEffectFunction _applyOrRemoveEffectDelegate = null!;

    public delegate void ApplyDamageCapFunction(nint attacker, nint hitInstance);
    public IHook<ApplyDamageCapFunction> HOOK_ApplyDamageCap = null!;
    private ApplyDamageCapFunction _applyDamageCapDelegate = null!;

    private bool _draconicReleaseActive = false;

    private const uint SIEGFRIED_CHAR_ID = 0x11102;
    private const uint DRACONIC_RELEASE_SKILL_ID = 0x6A4;
    private const uint UNCAPPED_DAMAGE_VALUE = 999999;

    private nint _lastKnownPlayerEntity = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesWritten);

    private static readonly IntPtr _selfProcessHandle = GetCurrentProcess();

    private static bool TrySafeRead<T>(nuint address, out T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        byte[] buffer = new byte[size];
        bool ok = ReadProcessMemory(_selfProcessHandle, (IntPtr)address, buffer, size, out int bytesRead) && bytesRead == size;
        value = ok ? MemoryMarshal.Read<T>(buffer) : default;
        return ok;
    }

    private static bool TrySafeWrite<T>(nuint address, T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        byte[] buffer = new byte[size];
        MemoryMarshal.Write(buffer, value);
        return WriteProcessMemory(_selfProcessHandle, (IntPtr)address, buffer, size, out int bytesWritten) && bytesWritten == size;
    }

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

        var imageBase = Process.GetCurrentProcess().MainModule!.BaseAddress;

        // Feature 1: combo resolver
        target.AddMainModuleScan("55 41 57 41 56 56 57 53 48 81 EC ?? ?? 00 00 48 8D AC 24 ?? 00 00 00 48 C7 85 ?? ?? 00 00 FE FF FF FF 44 89 CE 45 89 C6 89 D3 49 89 CF 48 8B 05 ?? ?? ?? ?? 80 B8 0C 02 00 00 00 74 23", (result) =>
        {
            var addr = imageBase + result.Offset;
            _resolveAttackIdDelegate = ResolveAttackId_Hook;
            HOOK_ResolveAttackId = _hooks!.CreateHook<ResolveAttackIdFunction>(_resolveAttackIdDelegate, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ResolveAttackId at 0x{addr.ToInt64():X}", System.Drawing.Color.LimeGreen);
        });

        // Feature 2a: buff effect apply/remove
        target.AddMainModuleScan("55 41 57 41 56 41 55 41 54 56 57 53 48 81 EC D8 00 00 00 48 8D AC 24 80 00 00 00 48 83 E4 E0 48 89 E3 48 89 AB C8 00 00 00 48 C7 45 50 FE FF FF FF 48 8B 05 ?? ?? ?? ?? F6 40 0B 01 0F 85 F0 09 00 00 49 89 CE 8B 49 10 85 C9 0F 84 E2 09 00 00 49 8B 46 18 89 53 2C 49 8B 7E 20 48 8B 15 ?? ?? ?? ?? FF C9 44 89 43 74 48 8B 72 48 48 8B 34 CE 48 39 FE 0F 85 B9 09 00 00 48 8B 52 20 48 8B 0C CA 48 39 C1 0F 85 A8 09 00 00", (result) =>
        {
            var addr = imageBase + result.Offset;
            // System.Windows.Forms.MessageBox.Show($"About to hook ApplyOrRemoveEffect at 0x{addr.ToInt64():X}\nAttach x64dbg now, then click OK.");
            _applyOrRemoveEffectDelegate = ApplyOrRemoveEffect_Hook;
            HOOK_ApplyOrRemoveEffect = _hooks!.CreateHook<ApplyOrRemoveEffectFunction>(_applyOrRemoveEffectDelegate, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ApplyOrRemoveEffect at 0x{addr.ToInt64():X}", System.Drawing.Color.LimeGreen);
        });

        // Feature 2b: damage cap function
        target.AddMainModuleScan("41 57 41 56 41 55 41 54 56 57 55 53 48 81 EC 88 00 00 00 C5 78 29 4C 24 70 C5 78 29 44 24 60 C5 F8 29 7C 24 50 C5 F8 29 74 24 40 49 89 D5 49 89 CF 48 8B 82 D8 00 00 00 48 BF 00 00 00 00 80 10 00 00 48 85 F8 75 ??", (result) =>
        {
            var addr = imageBase + result.Offset;
            _applyDamageCapDelegate = ApplyDamageCap_Hook;
            HOOK_ApplyDamageCap = _hooks!.CreateHook<ApplyDamageCapFunction>(_applyDamageCapDelegate, addr).Activate();
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

        if (!TrySafeRead((nuint)(@this + 0x264C), out uint rightClickFlag))
            return result;
        if (rightClickFlag != 1)
            return result;

        if (!TrySafeRead((nuint)(@this + 0x76B0), out nint subObj))
            return result;
        if (subObj == 0)
            return result;

        if (!TrySafeRead((nuint)(subObj + 0x40), out uint linkState))
            return result;

        if (linkState == 0x2B)
            return 0xAA;

        return result;
    }

    // ---- Feature 2a ----
    private void ApplyOrRemoveEffect_Hook(nint rcx, int mode, int r8d, nint r9)
    {
        HOOK_ApplyOrRemoveEffect.OriginalFunction(rcx, mode, r8d, r9);
        
        if (!_configuration.EnableDraconicReleaseCapBypass || rcx == 0)
            return;

        if (!TrySafeRead((nuint)(rcx + 0x58), out uint charId))
            return;
        if (!TrySafeRead((nuint)(rcx + 0x60), out uint skillId))
            return;

        if (charId == SIEGFRIED_CHAR_ID && skillId == DRACONIC_RELEASE_SKILL_ID)
        {
            _draconicReleaseActive = (mode == 0);
        }
    }

    // ---- Feature 2b ----
    private void ApplyDamageCap_Hook(nint attacker, nint hitInstance)
    {
        HOOK_ApplyDamageCap.OriginalFunction(attacker, hitInstance);

        if (!_configuration.EnableDraconicReleaseCapBypass || !_draconicReleaseActive || hitInstance == 0)
            return;

        if (attacker != (nint)(_lastKnownPlayerEntity + 0xBF0))
            return;

        TrySafeWrite((nuint)(hitInstance + 0x264), UNCAPPED_DAMAGE_VALUE);
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