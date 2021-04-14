using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using p4gpc.xpshare.Configuration;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.Enums;
using Reloaded.Hooks.Definitions.X86;
using Reloaded.Memory;
using Reloaded.Memory.Sigscan;
using Reloaded.Memory.Sources;
using Reloaded.Mod.Interfaces;
using static Reloaded.Hooks.Definitions.X86.FunctionAttribute;

namespace p4gpc.xpshare
{
    public class XpShare
    {
        private static readonly string[] MemberNames = { "", "Protagonist", "Yosuke", "Chie", "Yukiko", "Rise", "Kanji", "Naoto", "Teddie" };

        /// <summary>
        /// Current mod configuration.
        /// </summary>
        public Config Configuration { get; set; }

        // For calling C# code from ASM.
        private IReverseWrapper<XpAddedFunction> _reverseWrapper;

        // For Reading/Writing Memory
        private IMemory  _memory = new Memory();

        // For manipulating XP Share Hook.
        private IAsmHook _asmHook;

        // Process base address; this is normally a constant 0x400000 unless ASLR gets suddenly enabled.
        private int _baseAddress;

        // Provides logging functionality.
        private ILogger _logger;

        public XpShare(ILogger logger, IReloadedHooks hooks, Config configuration)
        {
            Configuration = configuration;
            _logger = logger;

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
                _logger.WriteLine("[xpshare] An error occured trying to find the function address. Not initializing." + exception.Message, Color.Red);
                return;
            }
            
            string[] function =
            {
                $"use32",
                // Not always necessary but good practice;
                // just in case the parent function doesn't preserve them.
                $"{hooks.Utilities.PushCdeclCallerSavedRegisters()}", 
                $"{hooks.Utilities.GetAbsoluteCallMnemonics(XpAdded, out _reverseWrapper)}",
                $"{hooks.Utilities.PopCdeclCallerSavedRegisters()}",
            };

            _asmHook = hooks.CreateAsmHook(function, functionAddress, AsmHookBehaviour.ExecuteFirst).Activate();
        }

        // Provided for completeness.
        public void Suspend() => _asmHook?.Disable();
        public void Resume()  => _asmHook?.Enable();

        private void XpAdded(int esi)
        {
            try
            {
                // TODO: You're looking for the function using signatures, however there are still hardcoded addresses down here. You either go in part way or all the way :P
                LogVerbose("Xp added starting");
                // Get how much xp was added
                _memory.SafeRead((IntPtr)(esi + 120), out int amountAdded);
                LogVerbose("The protagonist gained " + amountAdded + " xp");
                int amountToAdd = (int)(Math.Round(amountAdded * Math.Abs(Configuration.xpScale)));

                // Get who is in the party
                StructArray.FromPtr((IntPtr)0x49DC3C4 + _baseAddress, out short[] inParty, 3);
                LogVerbose("These are in the party: " + MemberNames[inParty[0]] + ", " + MemberNames[inParty[1]] + ", " +  MemberNames[inParty[2]]);

                // Get the current day and use that to determine who is unlocked
                int dayAddress = 0x49DDC9C + _baseAddress;
                _memory.SafeRead((IntPtr)dayAddress, out short day);
                var unlockedParty = new List<short>();

                // Yosuke
                if (day >= 17) unlockedParty.Add(2);

                // Chie
                if (day >= 18) unlockedParty.Add(3);

                // Yukiko
                if (day >= 30) unlockedParty.Add(4);

                // Kanji
                if (day >= 66) unlockedParty.Add(6);

                // Teddie
                if (day >= 101) unlockedParty.Add(8);

                // Naoto
                if (day >= 189) unlockedParty.Add(7);

                // Work out which members are therefore eligible to get xp 
                short[] inactiveParty = unlockedParty.Except(inParty).ToArray();

                // Add xp to them
                int xpLocation = 0x49DD114 + _baseAddress;
                foreach (short member in inactiveParty)
                {
                    // If there isn't a full party there will be zeroes instead of member ids so ignore them
                    if (member <= 0) 
                        continue;

                    // Get their current xp
                    _memory.SafeRead((IntPtr)xpLocation + (member - 2) * 132, out int currentXp);
                    // Add the xp
                    // Xp location is the location of Yosuke's so remove 2 (Yosuke's id) from id
                    _memory.SafeWrite((IntPtr)xpLocation + (member - 2) * 132, currentXp + amountToAdd);
                    LogVerbose("Added " + amountToAdd + " xp to " + MemberNames[member]);
                }
            }
            catch (Exception exception)
            {
                _logger.WriteLine("[xpshare] There was an error whilst trying to add xp\n[xpshare] " + exception.Message, _logger.ColorRed);
            }
        }

        private void LogVerbose(String message)
        {
            if (Configuration.verbose)
            {
                _logger.WriteLine("[xpshare] " + message);
            }
        }
        
        [Function(Register.esi, Register.edi, StackCleanup.Callee)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void XpAddedFunction(int esi);
    }
}
