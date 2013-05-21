using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text.RegularExpressions;

using apmcomms;

using Mono.Options;


namespace apmconfig
{
	class MainClass
	{

		MAVLink comPort = new MAVLink();


		OptionSet cmdLineOpts;
		string comPortName = "";
		int comPortSpeed = 115200;

		bool deleteSourceLogsWhenFinished = true; //TODO get from command line
		int skipFileSize = 256; //get from command line
		int verbosity = 1;




		public static void Main (string[] args)
		{

			MainClass monster = new MainClass ();

			if (monster.handleCommandLineArgs (args)) {
				monster.beginProcessing ();
			}
		}


		public bool handleCommandLineArgs(string[] args)
		{  
			bool okToBegin = true;

			cmdLineOpts = new OptionSet () {
				{"p|port=",
					"Name of the port to use for communicating with ArduPilot e.g. 'tty.usbmodem1a12131'",
					(string v) => {
						reportStatus("comPortName: '" + v + "'");
						comPortName = v;
					}
				},
				{"b|bps=",
					"Port speed to use when communicating with ArduPilot, an integer e.g. '115200'",
					(int v) => comPortSpeed = v 
				},
				{"e|erase=",
					"Erase logs from ArduPilot when finished loading, true/false",
					(bool v) => deleteSourceLogsWhenFinished = v
				},
				{"s|skip=",
					"Minimum size log to process eg 256. Anything smaller is skipped.",
					(int v) => skipFileSize = v
				},

				{ "v", "increase debug message verbosity",
					v => { 
						if (v != null) ++verbosity; 
					} 
				},
				{ "h|help",  "show this message and exit", 
					v => { 
						okToBegin = false;
						reportStatus ("Usage: apmconfig [OPTIONS]");
						cmdLineOpts.WriteOptionDescriptions(Console.Out);
					}
				},
			};

			List<string> extra;
			try {
				extra = cmdLineOpts.Parse (args);
			}
			catch (OptionException e) {
				reportError ("apmlog:\n" + e.Message + "\nTry `apmconfig --help' for more information.");
				okToBegin = false;
			}

			return okToBegin;
		}

		public void beginProcessing()
		{
			if (0 == comPortName.Length) {
				string[] portNames = apmcomms.Comms.SerialPort.GetPortNames ();
				foreach (string portName in portNames) {
					comPortName = portName;
				}
			}
			reportStatus ("***** Connecting to MAV on port: " + comPortName + "*****");
			getParameters (comPortName,comPortSpeed); //"tty.usbmodem1a12131",115200);
		}

		private void getParameters (string portName, int portSpeed)
		{
			comPort.BaseStream.PortName = portName;
			comPort.BaseStream.BaudRate = portSpeed;

			comPort.Open (true);

		}

		private void reportStatus(String status) 
		{
			Console.WriteLine (status);
		}


		private void reportError(String error) 
		{
			Console.WriteLine (error);
			Console.Error.WriteLine(error);
		}
	}


}
