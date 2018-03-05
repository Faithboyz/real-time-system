#region
//
// BlueWave.Interop.Asio by Rob Philpott. Please send all bugs/enhancements to
// rob@bigdevelopments.co.uk.  This file and the code contained within is freeware and may be
// distributed and edited without restriction. You may be bound by licencing restrictions
// imposed by Steinberg - check with them prior to distributing anything.
// 

#endregion

using System;
using System.Threading;
using System.Diagnostics;
using BlueWave.Interop.Asio;
using EricOulashin;

namespace BlueWave.Interop.Asio.Test
{
	/// <summary>
	/// A console application for audio effects
	/// </summary>
	public class GuitarEffects
	{
        // we'll store the input samples in a 2d array (one dimension is the buffer index,
        // the other is the delay count)
		private static float[,] _delayBuffer;

        // delay lengths buffers
        // delay in whole buffers
        private static int _FBDelay = 32;
        private static int _RDelay = 128;
        private static float[,] _delayFBbuffer;

        private static float[,] _delayINbuffer;
        private static float[,] _delayOUTbuffer;

        // phaser buffers
        // 4 stage
        //private static double[] R = new double[4] { 0.9, 0.98, 0.8, 0.9 };
        //private static double[] baseResFreq = new double[4] { 300, 800, 1000, 4000 };
        // 8 stage
        //private static double[] R = new double[8] { 0.9, 0.95, 0.98, 0.9, 0.8, 0.98, 0.9, 0.9 };
        //private static double[] baseResFreq = new double[8] { 100, 300, 800, 850, 1000, 1200, 2000, 4000 };
        // 12 stage
        private static double[] R = new double[12] { 0.9, 0.85, 0.95, 0.85, 0.9, 0.95, 0.8, 0.95, 0.98, 0.8, 0.9, 0.95 };
        private static double[] baseResFreq = new double[12] { 100, 250, 450, 900, 920, 1000, 1200, 1500, 1800, 2000, 3500, 4000 };
        private static int numStages = R.Length;
        private static float[,] xold = new float[numStages, 2];
        private static float[,] yold = new float[numStages, 2];
        private static float[] x = new float[1000];
        private static float[] y = new float[1000];
        private static double phaserScaleFrequency = 0;
        private static double phaserSweepAngle = 0;

        private enum effect {none, delay, flanger, phaser, reverb};
        private static effect effectType;

        private enum effectDirection { increasing, decreasing };
        private static effectDirection phaseDirection = effectDirection.increasing;

		// how many buffers to keep for delay purposes
		private const int MaxBuffers = 500;

        // a counter to keep track of where we are in the delay array
        private static int _counter;

        // create a wave file for recording purposes
        private static WAVFile wav = new WAVFile();
        
        
        // STAThread is ESSENTIAL to make this work
		[STAThread] public static void Main(string[] args)
		{
            // no messing, this is high priority stuff
			Thread.CurrentThread.Priority = ThreadPriority.Highest;
            Process myP = Process.GetCurrentProcess();
            myP.ProcessorAffinity = (IntPtr)1;
            
			// make sure we have at least one ASIO driver installed
			if (AsioDriver.InstalledDrivers.Length == 0)
			{
				Console.WriteLine("There appears to be no ASIO drivers installed on your system.");
				Console.WriteLine("If your soundcard supports ASIO natively, install the driver");
				Console.WriteLine("from the support disc. If your soundcard has no native ASIO support");
				Console.WriteLine("you can probably use the generic ASIO4ALL driver.");
				Console.WriteLine("You can download this from: http://www.asio4all.com/");
				Console.WriteLine("It's very good!");
				Console.WriteLine();
				Console.WriteLine("Hit Enter to exit...");
				Console.ReadLine();
				return;
			}

            // bingo, we've got at least one
			Console.WriteLine("Your system has the following ASIO drivers installed:");
			Console.WriteLine();

            // so iterate through them
			for (int index = 0; index < AsioDriver.InstalledDrivers.Length; index++)
			{
                // and display them
				Console.WriteLine(string.Format("  {0}. {1}", index + 1, AsioDriver.InstalledDrivers[index]));
			}

			Console.WriteLine();

			int driverNumber = 0;

            // get them to choose one
			while (driverNumber < 1 || driverNumber > AsioDriver.InstalledDrivers.Length)
			{
				// we'll keep telling them this until they make a valid selection
				Console.Write("Select which driver you wish to use (x for exit): ");
				ConsoleKeyInfo key = Console.ReadKey();
				Console.WriteLine();

				// deal with exit condition
				if (key.KeyChar == 'x') return;

				// convert from ASCII to int
				driverNumber = key.KeyChar - 48;
			}

			Console.WriteLine();
			Console.WriteLine("Using: " + AsioDriver.InstalledDrivers[driverNumber - 1]);
			Console.WriteLine();

			// load and activate the desited driver
			AsioDriver driver = AsioDriver.SelectDriver(AsioDriver.InstalledDrivers[driverNumber - 1]);

			// popup the driver's control panel for configuration
            driver.ShowControlPanel();

			// now dump some details
            Console.WriteLine("  Driver name = " + driver.DriverName);
            Console.WriteLine("  Driver version = " + driver.Version);
            Console.WriteLine("  Input channels = " + driver.NumberInputChannels);
            Console.WriteLine("  Output channels = " + driver.NumberOutputChannels);
            Console.WriteLine("  Min buffer size = " + driver.BufferSizex.MinSize);
            Console.WriteLine("  Max buffer size = " + driver.BufferSizex.MaxSize);
            Console.WriteLine("  Preferred buffer size = " + driver.BufferSizex.PreferredSize);
            Console.WriteLine("  Granularity = " + driver.BufferSizex.Granularity);
            Console.WriteLine("  Sample rate = " + driver.SampleRate);

			// get our driver wrapper to create its buffers
			driver.CreateBuffers(false);

			// write out the input channels
            Console.WriteLine("  Input channels found = " + driver.InputChannels.Length);
			Console.WriteLine("  ----");

            foreach (Channel channel in driver.InputChannels)
			{
				Console.WriteLine(channel.Name);
			}

			// and the output channels
            Console.WriteLine("  Output channels found = " + driver.OutputChannels.Length);
            Console.WriteLine("----");

            foreach (Channel channel in driver.OutputChannels)
			{
				Console.WriteLine(channel.Name);
			}

            Console.Write("Select which effect you wish to use (1 = delay, 2 = flanger, 3 = phaser, 4 = reverb): ");
            ConsoleKeyInfo useEffect = Console.ReadKey();
            if (useEffect.KeyChar == '1')
                effectType = effect.delay;
            else if (useEffect.KeyChar == '2')
                effectType = effect.flanger;
            else if (useEffect.KeyChar == '3')
                effectType = effect.phaser;
            else if (useEffect.KeyChar == '4')
                effectType = effect.reverb;
            else
                effectType = effect.none;



            // create standard sized buffers with a size of PreferredSize x MaxBuffers 
            _delayBuffer = new float[driver.BufferSizex.PreferredSize, MaxBuffers];

            // create a feedback buffer for the delay effect
            _delayFBbuffer = new float[driver.BufferSizex.PreferredSize, MaxBuffers];

            // create a input buffer for reverb effect
            _delayINbuffer = new float[driver.BufferSizex.PreferredSize, MaxBuffers];

            // create a output buffer for reverb effect
            _delayOUTbuffer = new float[driver.BufferSizex.PreferredSize, MaxBuffers];

            // this is our buffer fill event we need to respond to
            driver.BufferUpdate += new EventHandler(AsioDriver_BufferUpdate);

            // and off we go
            driver.Start();

            // create a wav file for recording
            wav.Create("test.wav", true, 44100, 16);

            // wait for enter key
            Console.WriteLine();
            Console.WriteLine("Press Enter to end");
			Console.ReadLine();

            // and all done
            driver.Stop();

            // close the wav file
            wav.Close();

        }



        /// <summary>
        /// Called when a buffer update is required
        /// </summary>
        private static void AsioDriver_BufferUpdate(object sender, EventArgs e)
		{
            // every time called, update the entire output buffer.
            // each buffer is going to be 2.902 ms long (for 128 sample buffer size)

			// the driver is the sender
            AsioDriver driver = sender as AsioDriver;
	
			// get the input channel and the stereo output channels
            Channel input = driver.InputChannels[1];
			Channel leftOutput = driver.OutputChannels[0];
			Channel rightOutput = driver.OutputChannels[1];

            // call the appropriate effect function
            if (effectType == effect.none)
                noEffect(input, leftOutput, rightOutput);
            else if (effectType == effect.delay)
                delayEffect(input, leftOutput, rightOutput);
            else if (effectType == effect.flanger)
                flangeEffect(input, leftOutput, rightOutput);
            else if (effectType == effect.phaser)
                phaserEffect(input, leftOutput, rightOutput);
            else if (effectType == effect.reverb)
                reverbEffect(input, leftOutput, rightOutput);

            // write the output of the effect to a wav file
            for (int index = 0; index < leftOutput.BufferSize; index++)
            {
                wav.AddSample_16bit((short)(65535 * leftOutput[index]));
                wav.AddSample_16bit((short)(65535 * rightOutput[index]));
            }
        }


        public static void noEffect(Channel input, Channel outputL, Channel outputR)
        {
            for (int index = 0; index < outputL.BufferSize; index++)
            {
                // pass the input to the output directly
                outputL[index] = input[index];
                outputR[index] = input[index];
            }            
        }

        public static void delayEffect(Channel input, Channel outputL, Channel outputR)
        {
            // the effect output
            float delayOut = 0;

            // increment the delay buffer counter
            _counter++;
            // and wrap the delay buffer counter
            if (_counter >= MaxBuffers) _counter = 0;

            for (int index = 0; index < outputL.BufferSize; index++)
			{
				// copy the input buffer to our delay array
				_delayBuffer[index, _counter] = input[index];

                // y[n] = gfb y[n-d] + x[n] + (gff - gfb) x[n-d]
                //gfb = 0.3
                //gff = 1

              
                // calculate the equation above
                if ((_counter - _RDelay) >=0 )
                    delayOut = (float)0.3*_delayFBbuffer[index, (_counter - _RDelay)] + input[index] + (float)0.7*_delayBuffer[index, (_counter - _RDelay)];
                else
                    delayOut = (float)0.3*_delayFBbuffer[index, (_counter - _RDelay + MaxBuffers)] + input[index] + (float)0.7*_delayBuffer[index, (_counter - _RDelay + MaxBuffers)];
                   

                // update the feedback buffer
                _delayFBbuffer[index, _counter] = delayOut;

                // write the output buffer with the effect output
                outputL[index] = delayOut; 
                outputR[index] = delayOut; 
			}
        }


        public static void phaserEffect(Channel input, Channel outputL, Channel outputR)
        {
            float a1 = 0;
            float a2 = 0;
            double theta = 0;
            double resFreq = 0;

            // the phaser only uses a delay of 2 samples to implement a 2nd order all pass filter
            // we will implement a 4, 8 or 12 stage phaser

            // the finite difference equation for the all pass filter is
            // y[n] + a1*y[n-1] + a2*y[n-2] = a2*x[n] + a1*x[n-1] + x[n-2]
            // y[n] = a2*x[n] + a1*x[n-1] + x[n-2] - a1*y[n-1] - a2*y[n-2]

            // implement 4, 8 or 12 stage phaser
            for (int stage = 0; stage < numStages; stage++)
            {
                x[0] = xold[stage, 0];
                x[1] = xold[stage, 1];               
                y[0] = yold[stage,0];
                y[1] = yold[stage,1];

                // set a1 and a2 for each stage
                resFreq = phaserScaleFrequency * baseResFreq[stage];
                theta = (resFreq * 2 * Math.PI) / 44100D;
                a1 = (float)(-2 * R[stage] * Math.Cos(theta));
                a2 = (float)(R[stage]*R[stage]);

                for (int index = 2; index < outputL.BufferSize + 2; index++)
                {
                    if (stage == 0)
                    {
                        x[index] = input[index - 2] + 0.7F*y[index];
                    }
                    else
                    {
                        x[index] = y[index];
                    }

                    y[index] = a2 * x[index] + a1 * x[index - 1] + x[index - 2] - a1 * y[index - 1] - a2 * y[index - 2];

                    // set the final output of the buffer
                    if (stage == (numStages - 1))
                    {
                        outputL[index - 2] = 1F*y[index] - 1.1F*input[index - 2];
                        outputR[index - 2] = 1F*y[index] - 1.1F*input[index - 2];
                    }
                }

                xold[stage, 0] = x[outputL.BufferSize];
                xold[stage, 1] = x[outputL.BufferSize + 1];
                yold[stage, 0] = y[outputL.BufferSize];
                yold[stage, 1] = y[outputL.BufferSize + 1];
                
            }

         
            if (phaseDirection == effectDirection.increasing)
                phaserSweepAngle += 0.01;
            else
                phaserSweepAngle -= 0.01;

           // sweep the phaser so that the base frequencies are multiplied by values in the range 1 to 4
            if (phaserSweepAngle > 6.2)
                phaseDirection = effectDirection.decreasing;
            else if (phaserSweepAngle < 0.2)
                phaseDirection = effectDirection.increasing;
           
            phaserScaleFrequency = 1.5D*Math.Sin(phaserSweepAngle) + 2.5D;
        }


        // ********************** INSERT YOUR CODE HERE ********************************
        public static void flangeEffect(Channel input, Channel outputL, Channel outputR)
        {


        }

        public static void reverbEffect(Channel input, Channel outputL, Channel outputR)
        {
            // the effect output
            float comb = 0;
          //  float allpass = 0;

            // increment the delay buffer counter
            _counter++;
            // and wrap the delay buffer counter
            if (_counter >= MaxBuffers) _counter = 0;

            for (int index = 0; index < input.BufferSize; index++)
            {
                // copy the input buffer to our delay array
                  _delayBuffer[index, _counter] = input[index];



                if ((_counter - _FBDelay) >= 0)
                {
                    // y[n] = x[n – d] + gy[n - d]
                    comb = _delayBuffer[index, (_counter - _FBDelay)] + (float)0.7 * _delayFBbuffer[index, (_counter - _FBDelay)];
                }
                else
                {
                    // y[n] = -gx[n] + x[n - d] + gy[n – d]
                    comb = -(float)0.7 * input[index] + _delayBuffer[index, (_counter - _FBDelay + MaxBuffers)] + (float)0.7 * _delayFBbuffer[index, (_counter - _FBDelay + MaxBuffers)];
                }

                // update the feedback buffer
                  _delayFBbuffer[index, _counter] = comb;

                // write the output buffer with the effect output
                outputL[index] = comb;
                outputR[index] = comb;
            }

        }
    }



    }

    

