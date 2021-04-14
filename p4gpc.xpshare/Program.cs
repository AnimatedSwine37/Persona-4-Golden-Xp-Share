using System;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;
using Reloaded.Mod.Interfaces.Internal;
using p4gpc.xpshare.Configuration;
using p4gpc.xpshare.Configuration.Implementation;
using Reloaded.Hooks.Definitions;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;
using Reloaded.Hooks.Definitions.Enums;
using System.Diagnostics;
using Reloaded.Hooks.Definitions.X86;
using System.Runtime.InteropServices;
using Reloaded.Memory.Sources;
using Reloaded.Memory;
using System.Linq;
using Reloaded.Memory.Sigscan;
using System.Drawing;
using System.Collections;

namespace p4gpc.xpshare
{
    public class Program : IMod
    {
        /// <summary>
        /// Your mod if from ModConfig.json, used during initialization.
        /// </summary>
        private const string MyModId = "p4gpc.xpshare";

        /// <summary>
        /// Used for writing text to the console window.
        /// </summary>
        private ILogger _logger;

        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private IModLoader _modLoader;

        /// <summary>
        /// Stores the contents of your mod's configuration. Automatically updated by template.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// An interface to Reloaded's the function hooks/detours library.
        /// See: https://github.com/Reloaded-Project/Reloaded.Hooks
        ///      for documentation and samples. 
        /// </summary>
        private IReloadedHooks _hooks;

        // The reverse wrapper that does things with XpAdded
        private IReverseWrapper<XpAddedFunction> _reverseWrapper;

        // Utilities
        private IReloadedHooksUtilities _utilities;

        // For reading and writing to memory of current p4g process
        private IMemory _memory;

        private IAsmHook _asmHook;

        private int _baseAddress;

        /// <summary>
        /// Entry point for your mod.
        /// </summary>
        public void Start(IModLoaderV1 loader)
        {
            //Debugger.Launch();
            _modLoader = (IModLoader)loader;
            _logger = (ILogger)_modLoader.GetLogger();
            _modLoader.GetController<IReloadedHooks>().TryGetTarget(out _hooks);
            _utilities = _hooks.Utilities;
            _memory = new Memory();
            // Your config file is in Config.json.
            // Need a different name, format or more configurations? Modify the `Configurator`.
            // If you do not want a config, remove Configuration folder and Config class.
            var configurator = new Configurator(_modLoader.GetDirectoryForModId(MyModId));
            _configuration = configurator.GetConfiguration<Config>(0);
            _configuration.ConfigurationUpdated += OnConfigurationUpdated;

            /* Your mod code starts here. */
            // Scan for the address of the xp add function
            long functionAddress;
            try
            {
                using var thisProcess = Process.GetCurrentProcess();
                LogVerbose("The process is: " + thisProcess);
                using var scanner = new Scanner(thisProcess, thisProcess.MainModule);
                _baseAddress = thisProcess.MainModule.BaseAddress.ToInt32();
                functionAddress = scanner.CompiledFindPattern("D2 5F 5E 5B 89 EC 5D C3 00 00 00 00").Offset + 7 + _baseAddress;
                LogVerbose("Found the function address at " + functionAddress);
            }
            catch (Exception exception)
            {
                _logger.WriteLine("[xpshare] An error occured trying to find the function address. Defaulting to an assumed one (may be incorrect causing errors\n[xpshare] " + exception.Message, Color.Red);
                functionAddress = 577442292 + _baseAddress;
            }
            
            string[] function =
            {
                $"use32",
                $"{_utilities.GetAbsoluteCallMnemonics(XpAdded, out _reverseWrapper)}",
            };

            _asmHook = _hooks.CreateAsmHook(function, functionAddress, AsmHookBehaviour.ExecuteFirst);
            _asmHook.Activate();
        }

        [Function(FunctionAttribute.Register.esi, FunctionAttribute.Register.edi, FunctionAttribute.StackCleanup.Callee)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void XpAddedFunction(int esi);

        private readonly String[] memberNames = { "", "Protagonist", "Yosuke", "Chie", "Yukiko", "Rise", "Kanji", "Naoto", "Teddie" };

        public void XpAdded(int esi)
        {
            try
            {
                LogVerbose("Xp added starting");
                // Get how much xp was added
                _memory.SafeRead((IntPtr)(esi + 120), out int amountAdded);
                LogVerbose("The protagonist gained " + amountAdded + " xp");
                int amountToAdd = (int)(amountAdded * Math.Abs(_configuration.xpScale));

                // Get who is in the party
                StructArray.FromPtr((IntPtr)77448132 + _baseAddress, out short[] inParty, 3);
                LogVerbose("These are in the party: " + memberNames[inParty[0]] + ", " + memberNames[inParty[1]] + ", " +  memberNames[inParty[2]]);

                // Get the current day and use that to determine who is unlocked
                int dayAddress = 77454492 + _baseAddress;
                _memory.SafeRead((IntPtr)dayAddress, out short day);
                ArrayList unlockedParty = new ArrayList();
                // Yosuke
                if(day >= 17)
                {
                    unlockedParty.Add((short)2);
                }
                // Chie
                if(day >= 18)
                {
                    unlockedParty.Add((short)3);
                }
                // Yukiko
                if(day >= 30)
                {
                    unlockedParty.Add((short)4);
                }
                // Kanji
                if (day >= 66)
                {
                    unlockedParty.Add((short)6);
                }
                // Naoto
                if(day >= 189)
                {
                    unlockedParty.Add((short)7);
                }
                // Teddie
                if(day >= 101)
                {
                    unlockedParty.Add((short)8);
                }

                // Work out which members are therefore eligible to get xp 
                short[] inactiveParty = ((short[])unlockedParty.ToArray(typeof(short))).Except(inParty).ToArray();

                // Add xp to them
                int xpLocation = 77451540 + _baseAddress;
                foreach (short member in inactiveParty)
                {
                    // If there isn't a full party there will be zeroes instead of member ids so ignore them
                    if(member > 0)
                    {
                        // Get their current xp
                        _memory.SafeRead((IntPtr)xpLocation + (member - 2) * 132, out int currentXp);
                        // Add the xp
                        // Xp location is the location of Yosuke's so remove 2 (Yosuke's id) from id
                        _memory.SafeWrite((IntPtr)xpLocation + (member - 2) * 132, currentXp + amountToAdd);
                        LogVerbose("Added " + amountToAdd + " xp to " + memberNames[member]);

                    }
                }
            }
            catch (Exception exception)
            {
                _logger.WriteLine("[xpshare] There was an error whilst trying to add xp\n[xpshare] " + exception.Message, Color.Red);
            }
        }

        // Function that writes to the log if logging is enabled
        public void LogVerbose(String message)
        {
            if (_configuration.verbose)
            {
                _logger.WriteLine("[xpshare] " + message);
            }
        }

        private void OnConfigurationUpdated(IConfigurable obj)
        {
            /*
                This is executed when the configuration file gets updated by the user
                at runtime.
            */

            // Replace configuration with new.
            _configuration = (Config)obj;
            _logger.WriteLine($"[{MyModId}] Config Updated: Applying");

            // Apply settings from configuration.
            // ... your code here.
        }

        /* Mod loader actions. */
        public void Suspend()
        {
            /*  Some tips if you wish to support this (CanSuspend == true)
             
                A. Undo memory modifications.
                B. Deactivate hooks. (Reloaded.Hooks Supports This!)
            */
        }

        public void Resume()
        {
            /*  Some tips if you wish to support this (CanSuspend == true)
             
                A. Redo memory modifications.
                B. Re-activate hooks. (Reloaded.Hooks Supports This!)
            */
        }

        public void Unload()
        {
            /*  Some tips if you wish to support this (CanUnload == true).
             
                A. Execute Suspend(). [Suspend should be reusable in this method]
                B. Release any unmanaged resources, e.g. Native memory.
            */
        }

        /*  If CanSuspend == false, suspend and resume button are disabled in Launcher and Suspend()/Resume() will never be called.
            If CanUnload == false, unload button is disabled in Launcher and Unload() will never be called.
        */
        public bool CanUnload() => false;
        public bool CanSuspend() => false;

        /* Automatically called by the mod loader when the mod is about to be unloaded. */
        public Action Disposing { get; }

        /* Contains the Types you would like to share with other mods.
           If you do not want to share any types, please remove this method and the
           IExports interface.
        
           Inter Mod Communication: https://github.com/Reloaded-Project/Reloaded-II/blob/master/Docs/InterModCommunication.md
        */
        public Type[] GetTypes() => new Type[0];

        /* This is a dummy for R2R (ReadyToRun) deployment.
           For more details see: https://github.com/Reloaded-Project/Reloaded-II/blob/master/Docs/ReadyToRun.md
        */
        public static void Main() { }
    }
}
