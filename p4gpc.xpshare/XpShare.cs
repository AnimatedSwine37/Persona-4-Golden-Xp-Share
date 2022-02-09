using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        // For doing hook stuff
        private IReloadedHooks _hooks;

        // For manipulating XP Share Hook.
        private IAsmHook _asmHook;

        // Address where the day is stored
        private IntPtr _dayAddress;

        // Pointer to the day address
        private IntPtr _dayPtr;

        // Address where the current xp ammount is stored
        private IntPtr _xpAddress;

        // Pointer to the stat info stuff
        private IntPtr _statsPtr;

        // Start address where all party information is
        private IntPtr _partyAddress;

        // A pointer to the party address as the address doesn't get loaded until the game loads
        private IntPtr _partyPtr;

        // Provides utility stuff
        Utils _utils;

        public XpShare(Utils utils, IMemory memory, IReloadedHooks hooks, Config configuration)
        {
            Configuration = configuration;
            _utils = utils;
            _memory = memory;
            _hooks = hooks;

            List<Task> initTasks = new List<Task>();
            initTasks.Add(Task.Run(() => InitXpHook()));
            initTasks.Add(Task.Run(() => InitDateLocation()));
            initTasks.Add(Task.Run(() => InitXpLocation()));
            initTasks.Add(Task.Run(() => InitPartyLocation()));
            Task.WaitAll(initTasks.ToArray()); 
        }

        private void InitXpHook()
        {
            long functionAddress = _utils.SigScan("55 ?? ?? 83 EC 08 53 56 57 ?? ?? 89 55 ?? B9 ?? ?? ?? ?? E8 ?? ?? ?? ??", "xp added");
            if (functionAddress == -1)
                return;

            string[] function =
            {
                $"use32",
                // Not always necessary but good practice;
                // just in case the parent function doesn't preserve them.
                $"{_hooks.Utilities.PushCdeclCallerSavedRegisters()}",
                $"{_hooks.Utilities.GetAbsoluteCallMnemonics(XpAdded, out _reverseWrapper)}",
                $"{_hooks.Utilities.PopCdeclCallerSavedRegisters()}",
            };

            _asmHook = _hooks.CreateAsmHook(function, functionAddress, AsmHookBehaviour.ExecuteFirst).Activate();
        }

        // Find the location of the current date in game
        private void InitDateLocation()
        {
            long datePtrAddress = _utils.SigScan("8B 0D ?? ?? ?? ?? ?? F6 0F BF 01", "date pointer");
            if (datePtrAddress == -1)
            {
                Suspend();
                return;
            }
            _memory.SafeRead((IntPtr)(datePtrAddress + 2), out _dayPtr);
        }

        private void InitXpLocation()
        {
            long ptrAddress = _utils.SigScan("A1 ?? ?? ?? ?? 8B 70 ?? ?? ?? 66 90", "stats pointer");
            if(ptrAddress == -1)
            {
                Suspend();
                return;
            }
            _memory.SafeRead((IntPtr)(ptrAddress + 1), out _statsPtr);
        }

        private void InitPartyLocation()
        {
            long address = _utils.SigScan("8B 0D ?? ?? ?? ?? B8 01 00 00 00 66 89 06 ?? ?? 0F B7 41 ??", "in party pointer");
            if(address == -1)
            {
                Suspend();
                return;
            }
            _memory.SafeRead((IntPtr)(address + 2), out _partyPtr);
        }

        // Provided for completeness.
        public void Suspend() => _asmHook?.Disable();
        public void Resume()  => _asmHook?.Enable();

        private void XpAdded(int esi)
        {
            // Get the xp address (has to be done after the game has initialised)
            if(_xpAddress == IntPtr.Zero)
            {
                _memory.SafeRead(_statsPtr, out _xpAddress);
                _xpAddress += 0xE0;
                _utils.LogDebug($"The xp info starts at 0x{_xpAddress:X}");
            }
            
            // Get the in party address (has to be done after the game has initialised)
            if (_partyAddress == IntPtr.Zero)
            {
                _memory.SafeRead(_partyPtr, out _partyAddress);
                _partyAddress += 4;
                _utils.LogDebug($"The in party info starts at 0x{_partyAddress:X}");
            }

            // Get the day address (has to be done after the game has initialised)
            if (_dayAddress == IntPtr.Zero)
            {
                _memory.SafeRead(_dayPtr, out _dayAddress);
                _utils.LogDebug($"The day is at 0x{_dayAddress:X}");
            }

            try
            {
                _utils.LogDebug("Xp added starting");
                // Get how much xp was added
                _memory.SafeRead((IntPtr)(esi + 120), out int amountAdded);
                _utils.LogDebug("The protagonist gained " + amountAdded + " xp");
                int amountToAdd = (int)Math.Round(amountAdded * Math.Abs(Configuration.xpScale));
                if(amountToAdd == 0) return;

                // Get who is in the party
                StructArray.FromPtr(_partyAddress, out short[] inParty, 3);
                _utils.LogDebug("These are in the party: " + MemberNames[inParty[0]] + ", " + MemberNames[inParty[1]] + ", " +  MemberNames[inParty[2]]);

                // Get the current day and use that to determine who is unlocked
                _memory.SafeRead((IntPtr)_dayAddress, out short day);
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
                foreach (short member in inactiveParty)
                {
                    // If there isn't a full party there will be zeroes instead of member ids so ignore them
                    if (member <= 0) 
                        continue;

                    // Get their current xp
                    _memory.SafeRead(_xpAddress + (member - 2) * 132, out int currentXp);
                    // Add the xp
                    // Xp location is the location of Yosuke's so remove 2 (Yosuke's id) from id
                    _memory.SafeWrite(_xpAddress + (member - 2) * 132, currentXp + amountToAdd);
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
