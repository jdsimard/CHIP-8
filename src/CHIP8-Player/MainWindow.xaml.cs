using System.Printing.IndexedProperties;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Drawing;
using JoelSimard.CHIP8;
using System.Windows.Threading;
using System.Timers;
using System.Threading;

using System.Diagnostics;
using System.Media;
using System.ComponentModel;


namespace CHIP8_Player
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 


    public partial class MainWindow : Window
    {
        // for holding the buffers used to draw and update the display image
        private byte[] rawImage;
        private WriteableBitmap bitmap;

        // the CHIP8-VirtualMachine object that will run loaded .ch8 programs
        private CHIP8_VirtualMachine chip8;

        // used for FPS and execution rate displayed at the bottom of the window
        private int framesSeen = 0;
        private int cyclesSeen = 0;

        // timers for redrawing the display window (fpsTimer) and updating the FPS count (fpsMonitorTimer)
        // since they need to interact with the UI, they cannot easily run in a separate thread without extra work
        // for now, I've gone with the simple solution of adding them to the dispatcher queue, as this will lead a usable, but fairly unstable, FPS
        private DispatcherTimer fpsTimer;
        private DispatcherTimer fpsMonitorTimer;
        
        // a BackgroundWorker for managing the CHIP8-VirtualMachine execution at a stable rate in another thread
        // virtual machine execution doesn't directly interact with the UI (display is rendered from the screenBuffer using fpsTimer), so it is straightforward to use the BackgroundWorker
        private BackgroundWorker bwCPU;

        // an object used as a lock for thread-safe access to the CHIP8_VirtualMachine.screenBuffer
        private readonly Object screenBuffer_lock = new Object();

        // settings for writing and rendering the display bitmap
        private PixelFormat pf = PixelFormats.BlackWhite;
        private int rawPixelWidth = 64;
        private int rawPixelHeight = 32;
        private int scale = 4;
        private int width;
        private int height;
        private int rawStride;

        // the filepath of the .ch8 program selected by the user
        private string selectedProgram = "";

        // holds the key press state, which is sent to the CHIP8-VirtualMachine whenever the fpsTimer triggers its callback
        private bool[] keypadState;


        // clear the debug monitor in the window
        private void DebugRefresh()
        {
            txtBlkDebug.Text = "Debug>> ";
        }



        // write a line to the debug monitor in the window
        private void DebugWriteline(string text)
        {
            txtBlkDebug.Text += text + Environment.NewLine + "Debug>> ";
        }




        public MainWindow()
        {
            InitializeComponent();

            // set initial conditions for UI components, and initialize a CHIP8_VirtualMachine object
            InitializeUI();
            BootCHIP8VM();
        }



        private void InitializeUI()
        {
            DebugRefresh();

            lblFPSMonitor.Content = "FPS: 0\t\tCHIP-8 Instructions Per Second: 0";

            // prepare the byte[] for writing to the bitmap, taking upscaling into account for array size
            width = rawPixelWidth * scale;
            height = rawPixelHeight * scale;
            rawStride = BitmapStride(pf, width);
            rawImage = new byte[rawStride * height];
            // the initial bitmap will be all "off" pixels
            for (int i = 0; i < rawImage.Length; i++)
            {
                rawImage[i] = 0x00;
            }
            // initialize bitmap, and use it as a source for the image component in the window
            bitmap = new WriteableBitmap(BitmapSource.Create(width, height, 96, 96, pf, null, rawImage, rawStride));
            imgChip8Display.Source = bitmap;

            // initialize keypad state
            keypadState = new bool[16];

            // setup fpsTimer and fpsTimer with callbacks for updating/rendering the display, and updating the FPS monitor
            fpsTimer = new DispatcherTimer();
            fpsTimer.Tick += UpdateDisplay;
            fpsTimer.Interval = new TimeSpan(0, 0, 0, 0, 16, 666); // 60 FPS (NOTE: this will not be a stable rate due to dispatcher queue)
            //fpsTimer.Interval = new TimeSpan(0, 0, 0, 0, 100, 0); // 10 FPS
            //fpsTimer.Interval = new TimeSpan(0, 0, 0, 0, 33, 333); // 30 FPS
            //fpsTimer.Interval = new TimeSpan(0, 0, 0, 0, 10, 0); // 100 FPS
            fpsMonitorTimer = new DispatcherTimer();
            fpsMonitorTimer.Tick += UpdateFPSMonitor;
            fpsMonitorTimer.Interval = new TimeSpan(0, 0, 2); // update fpsMonitor every ~2 seconds
        }

        // initialize the CHIP8_VirtualMachine object
        private void BootCHIP8VM()
        {
            //chip8CPUTimer = new System.Timers.Timer(new TimeSpan(0, 0, 0, 0, 1, 600)); // 600 Hz
            //chip8CPUTimer = new System.Timers.Timer(new TimeSpan(0, 0, 0, 0, 16, 0)); // 600 Hz
            //chip8CPUTimer.Elapsed += UpdateCPU;
            //chip8CPUTimer.AutoReset = true;

            // we want 60 timer ticks per second
            // 60 timer ticks per second = 600 CPU cycles per second / x CPU cycles per timer tick
            uint cyclesPerTimerTick = 600 / 60; // x = 600 CPU cycles per second / 60 timer ticks per second = 10 CPU cycles per timer tick
            chip8 = new CHIP8_VirtualMachine(cyclesPerTimerTick);

            DebugWriteline($"Loaded {selectedProgram}: {chip8.LoadProgram(selectedProgram).ToString()}");
        }




        // the number of bytes per scanline, rounded up to 4 bytes (or 32 bits) for read-efficiency on older CPUs
        // bytesPerPixel = ceil(bitsPerPixel/8) = floor(bitsPerPixel/8 + 7/8) = floor((bitsPerPixel + 7)/8)
        // bytesPerScanLine = width * bytesPerPixel
        // stride = 4 * ceil(bytesPerScanLine / 4) = 4 * floor(bytesPerScanLine/4 + 3/4) = 4 * floor((bytesPerScanLine + 3)/4)
        // => stride = 4 * floor((width * floor((bitsPerPixel + 7)/8) + 3)/4)
        // https://stackoverflow.com/questions/2185944/why-must-stride-in-the-system-drawing-bitmap-constructor-be-a-multiple-of-4
        private static int BitmapStride(PixelFormat pf, int width)
        {
            // doesn't work for bitsPerPixed < 8 because the pixels get packed together for efficiency - rounding before consider the total number of bitsPerScanLine leads to an erroniously large number
            /*int bytesPerPixel = (pf.BitsPerPixel + 7) / 8;
            int bytesPerScanLine = width * bytesPerPixel;
            return 4 * ((bytesPerScanLine + 3) / 4);*/

            int bytesPerScanLine = (width * pf.BitsPerPixel + 7) / 8;
            return 4 * ((bytesPerScanLine + 3) / 4);
        }




        /*private void UpdateCPU(object sender, EventArgs e)
        {
            for (int i = 0; i < 10; i++)
            {
                cyclesSeen++;
                chip8.ExecuteCycle();
            }
        }*/


        // callback for the CPU BackgroundWorker
        private void UpdateCPUbg(object sender, DoWorkEventArgs e)
        {
            // use a stopwatch for system clock resolution timing
            Stopwatch sw = new Stopwatch();
            long ticksPerMillisecond = Stopwatch.Frequency / 1000; // timer ticks per millisecond

            // a loop for executing the CHIP8-VirtualMachine at 600 Hz
            while (bwCPU.CancellationPending == false)  // will stop execution if it is ever requested from the main UI thread
            {
                // start the stopwatch and wait until approximately 1.666 ms have passed (600 Hz execution rate)
                sw.Start();
                while (sw.ElapsedTicks < ticksPerMillisecond * 1.666)
                {
                    continue;
                }
                // reset the timer, update the cycles seen (for fpsMonitor), and perform one fetch-decode-execute instruction cycle
                sw.Reset();
                cyclesSeen++;

                // Since a fetch-decode-execute cycle can change CHIP8_VirtualMachine.screenBuffer, and another thread reads from it, we obtain a lock to make the operation thread-safe
                lock (screenBuffer_lock)
                {
                    chip8.ExecuteCycle();
                }
            }
            
        }




        private void UpdateDisplay(object sender, EventArgs e)
        {
            framesSeen++; // update counter for FPS monitor

            // only update the display if a render is due
            if (chip8.IsRenderDue())
            {
                // render scale = 8
                /*for (int i = 0; i < chip8.screenBuffer.GetLength(0); i++)
                {
                    for (int j = 0; j < chip8.screenBuffer.GetLength(1); j++)
                    {
                        for (int k = 0; k < scale; k++)
                        {
                            if (chip8.screenBuffer[i, j] == 0)
                                rawImage[(scale * i + k) * rawStride + j] = 0x00;
                            else
                                rawImage[(scale * i + k) * rawStride + j] = 0xFF;
                        }
                    }
                }*/


                // render scale = 4
                // upscale each pixel in the CHIP8-VirtualMachine screenBuffer to a 4x4 pixel block
                // loop ordering to take advantage of memory locality (though it probably doesn't matter much here)
                int rawImageIndex = 0;

                // Since we read from CHIP8_VirtualMachine.screenBuffer, and another thread can modify it via ExecuteCycle(), we obtain a lock to make the operation thread-safe
                lock (screenBuffer_lock)
                {
                    // unset the render due flag, since we are about to update the display
                    chip8.UnsetRenderDueFlag();

                    for (int i = 0; i < chip8.screenBuffer.GetLength(0); i++)
                    {
                        for (int ki = 0; ki < scale; ki++)
                        {
                            for (int j = 0; j < chip8.screenBuffer.GetLength(1); j += 2)
                            {
                                if (chip8.screenBuffer[i, j] == 0)
                                    rawImage[rawImageIndex] = 0x00;
                                else
                                    rawImage[rawImageIndex] = 0xF0;
                                if (chip8.screenBuffer[i, j + 1] == 0)
                                    rawImage[rawImageIndex] |= 0x00;
                                else
                                    rawImage[rawImageIndex] |= 0x0F;

                                rawImageIndex++;
                            }
                        }
                    }
                }

                // render scale = 2
                /*int rawImageIndex = 0;
                for (int i = 0; i < chip8.screenBuffer.GetLength(0); i++)
                {
                    for (int ki = 0; ki < scale; ki++)
                    {
                        for (int j = 0; j < chip8.screenBuffer.GetLength(1); j += 4)
                        {
                            if (chip8.screenBuffer[i, j] == 0)
                                rawImage[rawImageIndex] = 0x00;
                            else
                                rawImage[rawImageIndex] = 0b11_00_00_00;

                            if (chip8.screenBuffer[i, j + 1] == 0)
                                rawImage[rawImageIndex] |= 0x00;
                            else
                                rawImage[rawImageIndex] |= 0b00_11_00_00;

                            if (chip8.screenBuffer[i, j + 2] == 0)
                                rawImage[rawImageIndex] |= 0x00;
                            else
                                rawImage[rawImageIndex] |= 0b00_00_11_00;

                            if (chip8.screenBuffer[i, j + 3] == 0)
                                rawImage[rawImageIndex] |= 0x00;
                            else
                                rawImage[rawImageIndex] |= 0b00_00_00_11;

                            rawImageIndex++;
                        }
                    }
                }*/

                // rewrite the bitmap associate the image component from the updated rawImage buffer
                bitmap.WritePixels(new Int32Rect(0, 0, 64 * scale, 32 * scale), rawImage, rawStride, 0);
            }

            // check the current keypress state
            keypadState[0] = Keyboard.IsKeyDown(Key.X); //0
            keypadState[1] = Keyboard.IsKeyDown(Key.D1); //1
            keypadState[2] = Keyboard.IsKeyDown(Key.D2); //2
            keypadState[3] = Keyboard.IsKeyDown(Key.D3); //3
            keypadState[4] = Keyboard.IsKeyDown(Key.Q); //4
            keypadState[5] = Keyboard.IsKeyDown(Key.W); //5
            keypadState[6] = Keyboard.IsKeyDown(Key.E); //6
            keypadState[7] = Keyboard.IsKeyDown(Key.A); //7
            keypadState[8] = Keyboard.IsKeyDown(Key.S); //8
            keypadState[9] = Keyboard.IsKeyDown(Key.D); //9
            keypadState[10] = Keyboard.IsKeyDown(Key.Z); //A
            keypadState[11] = Keyboard.IsKeyDown(Key.C); //B
            keypadState[12] = Keyboard.IsKeyDown(Key.D4); //C
            keypadState[13] = Keyboard.IsKeyDown(Key.R); //D
            keypadState[14] = Keyboard.IsKeyDown(Key.F); //E
            keypadState[15] = Keyboard.IsKeyDown(Key.V); //F
            // send current keypress state to the CHIP8-VirtualMachine, but not while the other thread controlling CHIP-8 execution is in the middle of an instruction cycle
            lock (screenBuffer_lock)
            {
                chip8.SendKeypadState(keypadState);
            }

            // if the CHIP8-VirtualMachine indicates that a beep should be playing, do so
            if (chip8.IsBeepPlaying())
            {
                Console.Beep(800, 10);
                //Task.Run(() => Console.Beep(800, 50));
            }
        }

        // callback for updating the FPS monitor
        private void UpdateFPSMonitor(object sender, EventArgs e)
        {
            lblFPSMonitor.Content = "FPS: " + framesSeen/2 + "\t\tCHIP-8 Instructions Per Second: " + cyclesSeen/2;
            framesSeen = 0;
            cyclesSeen = 0;
        }

        // callback for the "Play" button
        private void btnStartProgramClick(object sender, RoutedEventArgs e)
        {
            // enable/disable UI buttons
            btnStartProgram.IsEnabled = false;
            btnPauseProgram.IsEnabled = true;
            btnRebootProgram.IsEnabled = true;

            // start CHIP-8 execution in BackgroundWorker
            bwCPU.RunWorkerAsync();
            //chip8CPUTimer.Enabled = true;

            // start display update and FPS monitor timers
            fpsTimer.Start();
            fpsMonitorTimer.Start();
        }

        // callback for the "Pause" button
        private void btnPauseProgramClick(object sender, RoutedEventArgs e)
        {
            // enable/disable UI buttons
            btnPauseProgram.IsEnabled = false;
            btnStartProgram.IsEnabled = true;
            btnRebootProgram.IsEnabled = true;

            // request the BackgroundWorker to stop CHIP8-VirtualMachine execution (but leave the VM in its current state)
            bwCPU.CancelAsync();
            //chip8CPUTimer.Enabled = false;

            // stop display update and FPS monitor timers
            fpsTimer.Stop();
            fpsMonitorTimer.Stop();
        }

        // callback for the "Reboot" button
        private void btnRebootProgramClick(object sender, RoutedEventArgs e)
        {
            // enable/disable UI buttons
            btnRebootProgram.IsEnabled = false;
            btnStartProgram.IsEnabled = true;
            btnPauseProgram.IsEnabled = false;

            // request the BackgroundWorker to stop CHIP8-VirtualMachine execution
            bwCPU.CancelAsync();
            //chip8CPUTimer.Enabled= false;

            // stop display update and FPS monitor timers
            fpsTimer.Stop();
            fpsMonitorTimer.Stop();

            // this is reboot, so reinitialize the display and the CHIP8-VirtualMachine to their initial state
            InitializeUI();
            BootCHIP8VM();
        }

        // callback for the "Load Program" button
        private void btnSelectProgramClick(object sender, RoutedEventArgs e)
        {
            // if a CHIP-8 program is executing, cancel its thread and dispose of the object
            if (bwCPU != null)
            {
                bwCPU.CancelAsync();
                bwCPU.Dispose();
            }

            //chip8CPUTimer.Enabled = false;
            fpsTimer.Stop();
            fpsMonitorTimer.Stop();

            btnStartProgram.IsEnabled = false;
            btnPauseProgram.IsEnabled = false;
            btnRebootProgram.IsEnabled = false;

            // allow user to select a .ch8 file on their machine
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.DefaultExt = ".ch8";
            dlg.Filter = "CHIP-8 Program Files|*.ch8";
            //dlg.InitialDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            
            bool? result = dlg.ShowDialog();
            // if the user selected a .ch8 program, reinitialize the display and the CHIP8-VirtualMachine with the selected file
            if (result == true)
            {
                btnStartProgram.IsEnabled = true;
                selectedProgram = dlg.FileName;
                InitializeUI();
                BootCHIP8VM();

                // initialize and set the callback for the BackgroundWorker that will manage the CHIP8-VirtualMachine execution rate
                bwCPU = new BackgroundWorker();
                bwCPU.DoWork += UpdateCPUbg;
                bwCPU.WorkerSupportsCancellation = true;
            }
        }
    }
}