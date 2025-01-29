using System;
using System.IO;

namespace JoelSimard.CHIP8
{
    public class CHIP8_VirtualMachine
    {
        // The CHIP-8 virtual machines 4kB of memory
        private byte[] memory = new byte[4 * 1024];


        // CHIP-8 programs are assumed to start at memory address 0x0200; 0x0000 to 0x01FF is reserved for CHIP-8 interpreter
        private const ushort programEntry = 0x0200;
        // The data for character symbols [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, A, B, C, D, E, F] are stored in the memory such that they end just before 0x0200
        private const ushort charSymbolsOffset = (ushort)(programEntry - 16 * 5);


        // program counter register
        private ushort PC = programEntry;
        // address index register
        private ushort I = 0x0000;
        // 16 general purpose registers, V0 - VF
        private byte[] V = new byte[16];
        // delay timer register; when set, decrement 60 times per second
        private byte delayTimer = 0x00;
        // sound timer register; when value not 0, plays a beep and decrements 60 times per second; when setting, only responds to values 0x02 or above
        private byte soundTimer = 0x00;


        // callstack allowing nested subroutine calls in CHIP-8 programs; original spec was 12 calls deep
        private ushort[] callStack = new ushort[12];
        // the number of addresses written to the call stack, for the purposes of jumping back from a subroutine
        private int callStackLength = 0;


        // 2d array to hold pixel data written by CHIP-8 programs; public so that it is directly read by the user of this class
        public byte[,] screenBuffer = new byte[32, 64]; // [rows,cols]
        // indicates that pixels have been rewritten, and the display should be redrawn
        private bool displayRedrawFlag = false;


        // bools representing keys [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, A, B, C, D, E, F]; true if pressed, false otherwise; the key states will be sourced externally
        private bool[] keyPressed = new bool[16];


        // the CHIP-8 interpreter cycle speed is somewhat arbitrary, so the user will provide a value specifying how often (in terms of number of program instructions) the delay and sound timers should be decremented
        private uint cyclesPerTimerTick = 1;
        // count of how many instructions have executed since the delay and sound timers were last executed
        private uint timerCycleInstructionCount = 0;


        public CHIP8_VirtualMachine(uint cyclesPerTimerTick)
        {
            OpcodeClearScreenBuffer();

            this.cyclesPerTimerTick = cyclesPerTimerTick;

            InitializeKeyPressedStates();
            InitializeSymbolDataInMemory();
        }


        private void InitializeKeyPressedStates()
        {
            for (int i = 0; i < keyPressed.Length; i++)
            {
                keyPressed[i] = false;
            }
        }


        private void InitializeSymbolDataInMemory()
        {
            // 0
            memory[charSymbolsOffset] = 0xF0;
            memory[charSymbolsOffset + 1] = 0x90;
            memory[charSymbolsOffset + 2] = 0x90;
            memory[charSymbolsOffset + 3] = 0x90;
            memory[charSymbolsOffset + 4] = 0xF0;
            // 1
            memory[charSymbolsOffset + 5] = 0x20;
            memory[charSymbolsOffset + 6] = 0x60;
            memory[charSymbolsOffset + 7] = 0x20;
            memory[charSymbolsOffset + 8] = 0x20;
            memory[charSymbolsOffset + 9] = 0x70;
            // 2
            memory[charSymbolsOffset + 10] = 0xF0;
            memory[charSymbolsOffset + 11] = 0x10;
            memory[charSymbolsOffset + 12] = 0xF0;
            memory[charSymbolsOffset + 13] = 0x80;
            memory[charSymbolsOffset + 14] = 0xF0;
            // 3
            memory[charSymbolsOffset + 15] = 0xF0;
            memory[charSymbolsOffset + 16] = 0x10;
            memory[charSymbolsOffset + 17] = 0xF0;
            memory[charSymbolsOffset + 18] = 0x10;
            memory[charSymbolsOffset + 19] = 0xF0;
            // 4
            memory[charSymbolsOffset + 20] = 0x90;
            memory[charSymbolsOffset + 21] = 0x90;
            memory[charSymbolsOffset + 22] = 0xF0;
            memory[charSymbolsOffset + 23] = 0x10;
            memory[charSymbolsOffset + 24] = 0x10;
            // 5
            memory[charSymbolsOffset + 25] = 0xF0;
            memory[charSymbolsOffset + 26] = 0x80;
            memory[charSymbolsOffset + 27] = 0xF0;
            memory[charSymbolsOffset + 28] = 0x10;
            memory[charSymbolsOffset + 29] = 0xF0;
            // 6
            memory[charSymbolsOffset + 30] = 0xF0;
            memory[charSymbolsOffset + 31] = 0x80;
            memory[charSymbolsOffset + 32] = 0xF0;
            memory[charSymbolsOffset + 33] = 0x90;
            memory[charSymbolsOffset + 34] = 0xF0;
            // 7
            memory[charSymbolsOffset + 35] = 0xF0;
            memory[charSymbolsOffset + 36] = 0x10;
            memory[charSymbolsOffset + 37] = 0x20;
            memory[charSymbolsOffset + 38] = 0x40;
            memory[charSymbolsOffset + 39] = 0x40;
            // 8
            memory[charSymbolsOffset + 40] = 0xF0;
            memory[charSymbolsOffset + 41] = 0x90;
            memory[charSymbolsOffset + 42] = 0xF0;
            memory[charSymbolsOffset + 43] = 0x90;
            memory[charSymbolsOffset + 44] = 0xF0;
            // 9
            memory[charSymbolsOffset + 45] = 0xF0;
            memory[charSymbolsOffset + 46] = 0x90;
            memory[charSymbolsOffset + 47] = 0xF0;
            memory[charSymbolsOffset + 48] = 0x10;
            memory[charSymbolsOffset + 49] = 0xF0;
            // A
            memory[charSymbolsOffset + 50] = 0xF0;
            memory[charSymbolsOffset + 51] = 0x90;
            memory[charSymbolsOffset + 52] = 0xF0;
            memory[charSymbolsOffset + 53] = 0x90;
            memory[charSymbolsOffset + 54] = 0x90;
            // B
            memory[charSymbolsOffset + 55] = 0xE0;
            memory[charSymbolsOffset + 56] = 0x90;
            memory[charSymbolsOffset + 57] = 0xE0;
            memory[charSymbolsOffset + 58] = 0x90;
            memory[charSymbolsOffset + 59] = 0xE0;
            // C
            memory[charSymbolsOffset + 60] = 0xF0;
            memory[charSymbolsOffset + 61] = 0x80;
            memory[charSymbolsOffset + 62] = 0x80;
            memory[charSymbolsOffset + 63] = 0x80;
            memory[charSymbolsOffset + 64] = 0xF0;
            // D
            memory[charSymbolsOffset + 65] = 0xE0;
            memory[charSymbolsOffset + 66] = 0x90;
            memory[charSymbolsOffset + 67] = 0x90;
            memory[charSymbolsOffset + 68] = 0x90;
            memory[charSymbolsOffset + 69] = 0xE0;
            // E
            memory[charSymbolsOffset + 70] = 0xF0;
            memory[charSymbolsOffset + 71] = 0x80;
            memory[charSymbolsOffset + 72] = 0xF0;
            memory[charSymbolsOffset + 73] = 0x80;
            memory[charSymbolsOffset + 74] = 0xF0;
            // F
            memory[charSymbolsOffset + 75] = 0xF0;
            memory[charSymbolsOffset + 76] = 0x80;
            memory[charSymbolsOffset + 77] = 0xF0;
            memory[charSymbolsOffset + 78] = 0x80;
            memory[charSymbolsOffset + 79] = 0x80;
        }


        public bool LoadProgram(string filename)
        {
            if (File.Exists(filename))
            {
                FileStream fs = File.Open(filename, FileMode.Open, FileAccess.Read);
                
                int readByteAsInt = 0;
                int idx = 0; // file stream byte index
                while (readByteAsInt >= 0)
                {
                    readByteAsInt = fs.ReadByte();
                    memory[programEntry + idx] = (byte) readByteAsInt;
                    idx++;
                }
                fs.Close();
                return true;
            }
            else
                return false;
        }


        public bool IsRenderDue()
        {
            return displayRedrawFlag;
        }


        public void UnsetRenderDueFlag()
        {
            displayRedrawFlag = false;
        }


        public bool IsBeepPlaying()
        {
            return soundTimer > 0;
        }


        public byte ReadAddress(ushort address)
        {
            if (address >= 0 && address < memory.Length)
                return (byte)(memory[address]);
            else
                return (byte)0;
        }


        public void SendKeypadState(bool[] keypadState)
        {
            if (keypadState == null || keypadState.Length != 16)
                return;

            for (int i = 0; i < keypadState.Length; i++)
                keyPressed[i] = keypadState[i];
        }


        public void ExecuteCycle()
        {
            ushort nextOpcode = Fetch();
            DecodeAndExecute(nextOpcode);

            UpdateTimers();
        }


        private ushort Fetch()
        {
            ushort opcode = (ushort)((memory[PC] << 8) | memory[PC + 1]);
            PC += 2;
            return opcode;
        }


        private void DecodeAndExecute(ushort opcode)
        {
            switch (opcode >>> 12) // lead nibble
            {
                case 0x0:
                    switch (opcode)
                    {
                        case 0x00E0:
                            // clear the screen
                            OpcodeClearScreenBuffer();
                            break;
                        case 0x00EE:
                            // return from a subroutine
                            OpcodeReturnFromSubroutine();
                            break;
                        default:
                            // execute machine language subroutine at address (opcode & 0x0FFF)
                            // this is a VM with no underlying machine code, so this operation is not implemented
                            break;
                    }
                    break;
                case 0x1:
                    // jump to address (opcode & 0x0FFF)
                    OpcodeJumpTo(opcode);
                    break;
                case 0x2:
                    // execute subroutine at (opcode & 0x0FFF)
                    OpcodeExecuteSubroutine(opcode);
                    break;
                case 0x3:
                    // for 0x3XNN, skip the next instruction if the value of register VX equals NN
                    OpcodeSkipIfRegisterValueEqual(opcode);
                    break;
                case 0x4:
                    // for 0x4XNN, skip the next instruction if the value of register VX does not equal NN
                    OpcodeSkipIfRegisterValueNotEqual(opcode);
                    break;
                case 0x5:
                    if ((opcode & 0x000F) == 0)
                    {
                        // for 0x5XY0, skip the next instruction if the value of register VX is equal to the value of register VY
                        OpcodeSkipIfRegisterValueEqualsRegisterValue(opcode);
                    }
                    break;
                case 0x6:
                    // for 0x6XNN, store NN in register VX
                    OpcodeStoreValueInRegister(opcode);
                    break;
                case 0x7:
                    // for 0x7XNN, add the value NN to register VX
                    OpcodeAddValueToRegister(opcode);
                    break;
                case 0x8:
                    switch (opcode & 0x000F)
                    {
                        case 0x0:
                            // for 0x8XY0, store the value of register VY in register VX
                            OpcodeStoreRegisterValueInRegister(opcode);
                            break;
                        case 0x1:
                            // for 0x8XY1, set VX to (VX | VY)
                            OpcodeBitwiseOR(opcode);
                            break;
                        case 0x2:
                            // for 0x8XY2, set VX to (VX & VY)
                            OpcodeBitwiseAND(opcode);
                            break;
                        case 0x3:
                            // for 0x8XY3, set VX to (VX ^ VY)
                            OpcodeBitwiseXOR(opcode);
                            break;
                        case 0x4:
                            // for 0x8XY4, add the value of register VY to register VX, set VF to 0x01 if a carry occurs, set VF to 0x00 if a carry does not occur
                            OpcodeAddRegistersSetCarryFlag(opcode);
                            break;
                        case 0x5:
                            // for 0x8XY5, subtract the value of register VY from register VX, set VF to 0x00 if a borrow occurs, set VF to 0x01 if a borrow does not occur
                            OpcodeSubtractRegisterFromRegisterSetBorrowFlag(opcode);
                            break;
                        case 0x6:
                            // for 0x8XY6, store the value of register VY shifted right one bit in register VX, set register VF to the LSB prior to the shift, VY is unchanged
                            OpcodeStoreRegisterShiftedOneRightInOtherRegisterSetLSBFlag(opcode);
                            break;
                        case 0x7:
                            // for 0x8XY7, set register VX to the value of VY minus VX, set VF to 0x00 if a borrow occurs, set VF to 0x01 if a borrow does not occur
                            OpcodeSubtractRegisterFromRegisterSwappedSetBorrowFlag(opcode);
                            break;
                        case 0xE:
                            // for 0x8XYE, store the value of register VY shifted left one bit in register VX, set register VF to the MSB prior to the shift, VY is unchanged
                            OpcodeStoreRegisterShiftedOneLeftInOtherRegisterSetMSBFlag(opcode);
                            break;
                        default:
                            break;
                    }
                    break;
                case 0x9:
                    if ((opcode & 0x000F) == 0)
                    {
                        // for 0x9XY0, skip the next instruction if the value of register VX is not equal to the value of register VY
                        OpcodeSkipIfRegisterValueNotEqualsRegisterValue(opcode);
                    }
                    break;
                case 0xA:
                    // for 0xANNN, store memory address 0xNNN in register I
                    OpcodeStoreValueInIndexRegister(opcode);
                    break;
                case 0xB:
                    // for 0xBNNN, jump to address 0xNNN + V0
                    OpcodeJumpPlusOffset(opcode);
                    break;
                case 0xC:
                    // for 0xCXNN, set VX to a random number with a mask of 0xNN
                    OpcodeStoreRandomMaskedValue(opcode);
                    break;
                case 0xD:
                    // for 0xDXYN, draw a sprite at position VX, VY with 0xN bytes of sprite data starting at the address stored in I, set VF to 0x01 if any set pixels are changed to unset, and 0x00 otherwise
                    OpcodeDrawSpriteFromAddressRange(opcode);
                    break;
                case 0xE:
                    switch (opcode & 0x00FF)
                    {
                        case 0x9E:
                            // for 0xEX9E, skip the next instruction if the key corresponding to the hex value currently stored in register VX is pressed
                            OpcodeSkipIfKeyPressed(opcode);
                            break;
                        case 0xA1:
                            // for 0xEXA1, skip the next instruction if the key corresponding to the hex value currently stored in register VX is not pressed
                            OpcodeSkipIfKeyNotPressed(opcode);
                            break;
                        default:
                            break;
                    }
                    break;
                case 0xF:
                    switch (opcode & 0x00FF)
                    {
                        case 0x07:
                            // for 0xFX07, store the current value of the delay timer in register VX
                            OpcodeStoreDelayTimerInRegister(opcode);
                            break;
                        case 0x0A:
                            // for 0xFX0A, wait for a keypress and store the result in register VX
                            OpcodeWaitForKeypressAndStoreInRegister(opcode);
                            break;
                        case 0x15:
                            // for 0xFX15, set the delay timer to the value of register VX
                            OpcodeSetDelayTimerToRegisterValue(opcode);
                            break;
                        case 0x18:
                            // for 0xFX18, set the sound timer to the value of register VX
                            OpcodeSetSoundTimerToRegisterValue(opcode);
                            break;
                        case 0x1E:
                            // for 0xFX1E, add the value stored in register VX to register I
                            OpcodeAddRegisterValueToIndex(opcode);
                            break;
                        case 0x29:
                            // for 0xFX29, set I to the memory address of the sprite data corresponding to the hexadecimal digit stored in register VX
                            OpcodeSetIndexToAddressOfDigitSpriteDataInRegister(opcode);
                            break;
                        case 0x33:
                            // for 0xFX33, store the binary-coded decimal equivalent of the value stored in register VX at addresses I, I + 1, and I + 2
                            // https://en.wikipedia.org/wiki/Binary-coded_decimal
                            OpcodeStoreRegisterValueAsBCDInIndexedAddresses(opcode);
                            break;
                        case 0x55:
                            // for 0xFX55, store the values of registers V0 to VX inclusive in memory starting at address I, I is set to I + X + 1 after operation
                            OpcodeStoreRegisterRangeInAddressRangeAtIndex(opcode);
                            break;
                        case 0x65:
                            // for 0xFX65, fill registers V0 to VX inclusive with the values stored in memory starting at address I, I is set to I + X + 1 after operation
                            OpcodeStoreAddressRangeAtIndexInRegisterRange(opcode);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    break;
            }
        }


        private void OpcodeClearScreenBuffer()
        {
            displayRedrawFlag = true;
            for (int i = 0; i < screenBuffer.GetLength(0); i++)
            {
                for (int j = 0; j < screenBuffer.GetLength(1); j++)
                {
                    screenBuffer[i, j] = (byte)0;
                }
            }
        }


        private void OpcodeReturnFromSubroutine()
        {
            if (callStackLength > 0)
            {
                callStackLength--;
                PC = callStack[callStackLength];
            }
        }


        private void UpdateTimers()
        {
            timerCycleInstructionCount = (timerCycleInstructionCount + 1) % cyclesPerTimerTick;
            if (timerCycleInstructionCount == 0)
            {
                if (delayTimer > 0)
                {
                    delayTimer--;
                }
                if (soundTimer > 0)
                {
                    soundTimer--;
                    
                }
            }
        }


        private void OpcodeJumpTo(ushort opcode)
        {
            // jump to address (opcode & 0x0FFF)
            PC = (ushort)(opcode & 0x0FFF);
        }


        private void OpcodeExecuteSubroutine(ushort opcode)
        {
            // execute subroutine at (opcode & 0x0FFF)
            if (callStackLength < callStack.Length)
            {
                // save the current PC address in the call stack so that we can return later
                callStack[callStackLength] = PC;
                callStackLength++;
                // jump to the address of the subroutine
                PC = (ushort)(opcode & 0x0FFF);
            }
        }


        private void OpcodeSkipIfRegisterValueEqual(ushort opcode)
        {
            // for 0x3XNN, skip the next instruction if the value of register VX equals NN
            if (V[(opcode & 0x0F00) >>> 8] == (opcode & 0x00FF))
            {
                PC += 2;
            }
        }


        private void OpcodeSkipIfRegisterValueNotEqual(ushort opcode)
        {
            // for 0x4XNN, skip the next instruction if the value of register VX does not equal NN
            if ((V[(opcode & 0x0F00) >>> 8] == (opcode & 0x00FF)) == false)
            {
                PC += 2;
            }
        }


        private void OpcodeSkipIfRegisterValueEqualsRegisterValue(ushort opcode)
        {
            // for 0x5XY0, skip the next instruction if the value of register VX is equal to the value of register VY
            if (V[(opcode & 0x0F00) >>> 8] == V[(opcode & 0x00F0) >>> 4])
            {
                PC += 2;
            }
        }


        private void OpcodeStoreValueInRegister(ushort opcode)
        {
            // for 0x6XNN, store NN in register VX
            V[(opcode & 0x0F00) >>> 8] = (byte)(opcode & 0x00FF);
        }


        private void OpcodeAddValueToRegister(ushort opcode)
        {
            // for 0x7XNN, add the value NN to register VX
            V[(opcode & 0x0F00) >>> 8] += (byte)(opcode & 0x00FF);
        }


        private void OpcodeStoreRegisterValueInRegister(ushort opcode)
        {
            // for 0x8XY0, store the value of register VY in register VX
            V[(opcode & 0x0F00) >>> 8] = V[(opcode & 0x00F0) >>> 4];
        }


        private void OpcodeBitwiseOR(ushort opcode)
        {
            // for 0x8XY1, set VX to (VX | VY)
            V[(opcode & 0x0F00) >>> 8] |= V[(opcode & 0x00F0) >>> 4];
        }


        private void OpcodeBitwiseAND(ushort opcode)
        {
            // for 0x8XY2, set VX to (VX & VY)
            V[(opcode & 0x0F00) >>> 8] &= V[(opcode & 0x00F0) >>> 4];
        }


        private void OpcodeBitwiseXOR(ushort opcode)
        {
            // for 0x8XY3, set VX to (VX ^ VY)
            V[(opcode & 0x0F00) >>> 8] ^= V[(opcode & 0x00F0) >>> 4];
        }


        private void OpcodeAddRegistersSetCarryFlag(ushort opcode)
        {
            // for 0x8XY4, add the value of register VY to register VX, set VF to 0x01 if a carry occurs, set VF to 0x00 if a carry does not occur
            if (((uint)(V[(opcode & 0x0F00) >>> 8]) + (uint)(V[(opcode & 0x00F0) >>> 4])) > 0xFF)
                V[0x0F] = 0x01;
            else
                V[0x0F] = 0x00;
            V[(opcode & 0x0F00) >>> 8] = (byte)(V[(opcode & 0x0F00) >>> 8] + V[(opcode & 0x00F0) >>> 4]);
        }


        private void OpcodeSubtractRegisterFromRegisterSetBorrowFlag(ushort opcode)
        {
            // for 0x8XY5, subtract the value of register VY from register VX, set VF to 0x00 if a borrow occurs, set VF to 0x01 if a borrow does not occur
            if (V[(opcode & 0x00F0) >>> 4] > V[(opcode & 0x0F00) >>> 8])
                V[0x0F] = 0x00;
            else
                V[0x0F] = 0x01;
            V[(opcode & 0x0F00) >>> 8] = (byte)(V[(opcode & 0x0F00) >>> 8] - V[(opcode & 0x00F0) >>> 4]);
        }


        private void OpcodeStoreRegisterShiftedOneRightInOtherRegisterSetLSBFlag(ushort opcode)
        {
            // for 0x8XY6, store the value of register VY shifted right one bit in register VX, set register VF to the LSB prior to the shift, VY is unchanged
            V[0x0F] = (byte)(V[(opcode & 0x00F0) >>> 4] & 0x01);
            V[(opcode & 0x0F00) >>> 8] = (byte)(V[(opcode & 0x00F0) >>> 4] >>> 0x01);
        }


        private void OpcodeSubtractRegisterFromRegisterSwappedSetBorrowFlag(ushort opcode)
        {
            // for 0x8XY7, set register VX to the value of VY minus VX, set VF to 0x00 if a borrow occurs, set VF to 0x01 if a borrow does not occur
            if (V[(opcode & 0x0F00) >>> 8] > V[(opcode & 0x00F0) >>> 4])
                V[0x0F] = 0x00;
            else
                V[0x0F] = 0x01;
            V[(opcode & 0x0F00) >>> 8] = (byte)(V[(opcode & 0x00F0) >>> 4] - V[(opcode & 0x0F00) >>> 8]);
        }


        private void OpcodeStoreRegisterShiftedOneLeftInOtherRegisterSetMSBFlag(ushort opcode)
        {
            // for 0x8XYE, store the value of register VY shifted left one bit in register VX, set register VF to the MSB prior to the shift, VY is unchanged
            V[0x0F] = (byte)((byte)(V[(opcode & 0x00F0) >>> 4] & 0x80) >>> 0x07);
            V[(opcode & 0x0F00) >>> 8] = (byte)(V[(opcode & 0x00F0) >>> 4] << 0x01);
        }


        private void OpcodeSkipIfRegisterValueNotEqualsRegisterValue(ushort opcode)
        {
            // for 0x9XY0, skip the next instruction if the value of register VX is not equal to the value of register VY
            if ((V[(opcode & 0x0F00) >>> 8] == V[(opcode & 0x00F0) >>> 4]) == false)
            {
                PC += 2;
            }
        }


        private void OpcodeStoreValueInIndexRegister(ushort opcode)
        {
            // for 0xANNN, store memory address 0xNNN in register I
            I = (ushort)(opcode & 0x0FFF);
        }


        private void OpcodeJumpPlusOffset(ushort opcode)
        {
            // for 0xBNNN, jump to address 0xNNN + V0
            PC = (ushort)((opcode & 0x0FFF) + V[0x00]);
        }


        private void OpcodeStoreRandomMaskedValue(ushort opcode)
        {
            // for 0xCXNN, set VX to a random number with a mask of 0xNN
            var rand = new Random();
            V[(opcode & 0x0F00) >>> 8] = (byte)(rand.Next(0x100) & (byte)(opcode & 0x00FF));
        }


        private void OpcodeDrawSpriteFromAddressRange(ushort opcode)
        {
            // for 0xDXYN, draw a sprite at position VX, VY with 0xN bytes of sprite data starting at the address stored in I, set VF to 0x01 if any set pixels are changed to unset, and 0x00 otherwise
            // topleft pixel = (VX, VY)
            // width = 8 pixels (each row is a byte with each bit representing a pixel)
            // height = N pixels
            //displayRedrawFlag = true;

            int x = V[(opcode & 0x0F00) >>> 8] % 64;
            int y = V[(opcode & 0x00F0) >>> 4] % 32;
            V[0x0F] = 0x00;
            int height = opcode & 0x000F;
            for (int i = 0; i < height; i++)
            {
                byte pixelRow = memory[I + i];
                for (int j = 0; (j < 8) && (x + j < 64) && (y + i < 32); j++)
                {
                    if (screenBuffer[y + i, x + j] == 0x01)
                        if ((byte)((pixelRow >>> (7-j)) & 0x01) == 0x01)
                            V[0x0F] = 0x01;
                    screenBuffer[y + i, x + j] ^= (byte)((pixelRow >>> (7-j)) & 0x01);
                    //screenBuffer[y + i, x + j] = (byte)((pixelRow >>> j) & 0x01);
                }
            }
            displayRedrawFlag = true;
        }


        private void OpcodeSkipIfKeyPressed(ushort opcode)
        {
            // for 0xEX9E, skip the next instruction if the key corresponding to the hex value currently stored in register VX is pressed
            byte registerValue = V[(opcode & 0x0F00) >>> 8];

            if (registerValue < keyPressed.Length && keyPressed[registerValue] == true)
            {
                PC += 2;
            }
        }


        private void OpcodeSkipIfKeyNotPressed(ushort opcode)
        {
            // for 0xEXA1, skip the next instruction if the key corresponding to the hex value currently stored in register VX is not pressed
            byte registerValue = V[(opcode & 0x0F00) >>> 8];

            if (registerValue < keyPressed.Length && keyPressed[registerValue] == false)
            {
                PC += 2;
            }
        }


        private void OpcodeStoreDelayTimerInRegister(ushort opcode)
        {
            // for 0xFX07, store the current value of the delay timer in register VX
            V[(opcode & 0x0F00) >>> 8] = delayTimer;
        }


        private void OpcodeWaitForKeypressAndStoreInRegister(ushort opcode)
        {
            // for 0xFX0A, wait for a keypress and store the result in register VX
            for (byte i = 0; i < keyPressed.Length; i++)
            {
                if (keyPressed[i] == true)
                {
                    V[(opcode & 0x0F00) >>> 8] = i;
                    return;
                }
            }
            // No keypress, decrement the program counter to repeat the instruction next cycle
            PC -= 2;
        }


        private void OpcodeSetDelayTimerToRegisterValue(ushort opcode)
        {
            // for 0xFX15, set the delay timer to the value of register VX
            delayTimer = V[(opcode & 0x0F00) >>> 8];
        }


        private void OpcodeSetSoundTimerToRegisterValue(ushort opcode)
        {
            // for 0xFX18, set the sound timer to the value of register VX
            byte valueToSet = V[(opcode & 0x0F00) >>> 8];
            // sound timer register only responds to values above 0x01
            if (valueToSet > 0x01)
                soundTimer = valueToSet;
        }


        private void OpcodeAddRegisterValueToIndex(ushort opcode)
        {
            // for 0xFX1E, add the value stored in register VX to register I
            I += V[(opcode & 0x0F00) >>> 8];
        }


        private void OpcodeSetIndexToAddressOfDigitSpriteDataInRegister(ushort opcode)
        {
            // for 0xFX29, set I to the memory address of the sprite data corresponding to the hexadecimal digit stored in register VX
            byte digit = V[(opcode & 0x0F00) >>> 8];
            I = (ushort)(charSymbolsOffset + 5 * digit);
        }


        private void OpcodeStoreRegisterValueAsBCDInIndexedAddresses(ushort opcode)
        {
            // for 0xFX33, store the binary-coded decimal equivalent of the value stored in register VX at addresses I, I + 1, and I + 2
            // https://en.wikipedia.org/wiki/Binary-coded_decimal
            // most significant digit is stored in lowest address value (I)
            byte valueToStore = V[(opcode & 0x0F00) >>> 8];
            memory[I + 2] = (byte)(valueToStore % 10);
            valueToStore = (byte)((valueToStore - (valueToStore % 10)) / 10);
            memory[I + 1] = (byte)(valueToStore % 10);
            valueToStore = (byte)((valueToStore - (valueToStore % 10)) / 10);
            memory[I] = (byte)(valueToStore % 10);
        }


        private void OpcodeStoreRegisterRangeInAddressRangeAtIndex(ushort opcode)
        {
            // for 0xFX55, store the values of registers V0 to VX inclusive in memory starting at address I, I is set to I + X + 1 after operation
            byte rangeEnd = (byte)((opcode & 0x0F00) >>> 8);
            for (byte i = 0; i <= rangeEnd; i++)
            {
                memory[I + i] = V[i];
            }
            I += (ushort)(rangeEnd + 1);
        }


        private void OpcodeStoreAddressRangeAtIndexInRegisterRange(ushort opcode)
        {
            // for 0xFX65, fill registers V0 to VX inclusive with the values stored in memory starting at address I, I is set to I + X + 1 after operation
            byte rangeEnd = (byte)((opcode & 0x0F00) >>> 8);
            for (byte i = 0; i <= rangeEnd; i++)
            {
                V[i] = memory[I + i];
            }
            I += (ushort)(rangeEnd + 1);
        }
    }
}
