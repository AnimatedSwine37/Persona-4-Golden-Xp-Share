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
        private IMemory _memory;

        // For manipulating XP Share Hook.
        private IAsmHook _asmHook;

        // Process base address; this is normally a constant 0x400000 unless ASLR gets suddenly enabled.
        private int _baseAddress;

        // Address where the day is stored
        private int _dayAddress;

        // Address where the current xp ammount is stored
        private int _xpAddress;

        // Start address where all party information is
        private IntPtr _partyAddress;

        // Provides utility stuff
        Utils _utils;

        public XpShare(Utils utils, IMemory memory, IReloadedHooks hooks, Config configuration)
        {
            Configuration = configuration;
            _utils = utils;
            _memory = memory;

            long functionAddress;

            functionAddress = _utils.SigScan("55 ?? ?? 83 EC 08 53 56 57 ?? ?? 89 55 ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ??", "xp added");
            if (functionAddress == -1)
                return;

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
                // TODO Signature scan for these addresses as well
                _utils.LogDebug("Xp added starting");
                // Get how much xp was added
                _memory.SafeRead((IntPtr)(esi + 120), out int amountAdded);
                _utils.LogDebug("The protagonist gained " + amountAdded + " xp");
                int amountToAdd = (int)(Math.Round(amountAdded * Math.Abs(Configuration.xpScale)));
                if(amountToAdd == 0) return;

                // Get who is in the party
                StructArray.FromPtr((IntPtr)0x49DC3C4 + _baseAddress, out short[] inParty, 3);
                _utils.LogDebug("These are in the party: " + MemberNames[inParty[0]] + ", " + MemberNames[inParty[1]] + ", " +  MemberNames[inParty[2]]);

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
                    _utils.LogDebug("Added " + amountToAdd + " xp to " + MemberNames[member]);
                }
            }
            catch (Exception exception)
            {
                _utils.LogError("There was an error whilst trying to add xp", exception);
            }
        }
        
        [Function(Register.esi, Register.edi, StackCleanup.Callee)]
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void XpAddedFunction(int esi);
    }
}
