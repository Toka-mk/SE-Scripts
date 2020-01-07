using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System;
using VRage.Collections;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Game;
using VRage;
using VRageMath;

namespace IngameScript
{
	partial class Program : MyGridProgram
	{
		//from here

		List<IMyThrust> allThrust = new List<IMyThrust>();
		List<IMyThrust> atmoThrust = new List<IMyThrust>();
		List<IMyThrust> hydroThrust = new List<IMyThrust>();
		List<IMyThrust> ionThrust = new List<IMyThrust>();
		List<IMyThrust> upThrust = new List<IMyThrust>();
		List<IMyThrust> reverseThrust = new List<IMyThrust>();
		List<IMyThrust> auxThrust = new List<IMyThrust>();
		//IMyShipController controller;

		Dictionary<string, List<IMyThrust>> thrusters;
		Dictionary<string, bool> status;
		string statusstring;

		Dictionary<string, string> config;
		List<IMyTextPanel> LCDs = new List<IMyTextPanel>();

		public Program()
		{
			config = new Dictionary<string, string>
			{
				{"Tag", "Thruster"},
				{"LCD Tag", "Flight Mode"},
				{"Controller Tag", "Flight"},
			};

			UpdateBlocks();

			thrusters = new Dictionary<string, List<IMyThrust>>
			{
				{"all", allThrust},
				{"atmo", atmoThrust},
				{"hydro", hydroThrust},
				{"ion", ionThrust},
				{"grav", upThrust},
				{"cruise", reverseThrust},
				{"aux", auxThrust},
			};

			status = new Dictionary<string, bool>
			{
				{"all", true},
				{"atmo", true},
				{"hydro", true},
				{"ion", true},
				{"grav", true},
				{"cruise", true},
				{"aux", true},
			};

			string[] savedStatus = Storage.Split('\n');
			if (savedStatus.Length > 0)
			{
				foreach (string line in savedStatus)
				{
					string[] words = line.Split('=');
					if (words.Length != 2) continue;
					string key = words[0].Trim();
					bool value = words[1].Trim() == "True" ? true : false;
					if (status.ContainsKey(key)) status[key] = value;
				}
			}
		}

		public void Save()
		{
			string savedStatus = "";
			foreach (var keyValue in status)
			{
				savedStatus += keyValue.Key + "=" + keyValue.Value + "\n";
			}
			Storage = savedStatus;
		}

		public void Main(string arg, UpdateType updateSource)
		{
			arg = arg.ToLower();
			bool on;

			if (status.TryGetValue(arg, out on))
			{
				UpdateBlocks();
				on = !on;
				status[arg] = on;
				SetThrust(thrusters[arg], on);
				SetMode();
				Echo(arg.First().ToString().ToUpper() + arg.Substring(1) + ((arg == "grav" || arg == "cruise" ? !on : on) ? "On" : "Off") + "\n");
			}
			else Echo("Invalid Command\n");

			string message = "All Thrust        " + (status["all"] ? "On" : "Off")
							+ "                Cruise              " + (status["cruise"] ? "Off" : "On")
							+ "                Aux Thrust       " + (status["aux"] ? "On" : "Off")
							+ "\nAtmo Thrust    " + (status["atmo"] ? "On" : "Off")
							+ "                Hydro Thrust   " + (status["hydro"] ? "On" : "Off")
							+ "                Gravity Mode   " + (status["grav"] ? "Off" : "On");


			statusstring = ("All Thrust        " + (status["all"] ? "On" : "Off")
							+ "\nCruise               " + (status["cruise"] ? "Off" : "On")
							+ "\nAux Thrust        " + (status["aux"] ? "On" : "Off")
							+ "\nAtmo Thrust     " + (status["atmo"] ? "On" : "Off")
							+ "\nHydro Thrust    " + (status["hydro"] ? "On" : "Off")
							+ "\nGravity Mode    " + (status["grav"] ? "Off" : "On"));


			Echo(statusstring);
			
			//Echo(Storage);

			foreach (IMyTextSurface LCD in LCDs) LCD.WriteText(message);
		}

		void SetMode()
		{
			foreach (KeyValuePair<string, bool> s in status)
			{
				if (!s.Value) SetThrust(thrusters[s.Key], false);
			}
		}

		void SetThrust(List<IMyThrust> thrusters, bool on)
		{
			foreach (IMyThrust thrust in thrusters) thrust.Enabled = on;
		}

		void UpdateBlocks()
		{
			GetConfig(Me);

			allThrust.Clear();
			atmoThrust.Clear();
			hydroThrust.Clear();
			ionThrust.Clear();
			upThrust.Clear();
			reverseThrust.Clear();
			auxThrust.Clear();
			LCDs.Clear();

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
		}

		bool CollectBlocks(IMyTerminalBlock block)
		{
			IMyThrust thrust = block as IMyThrust;
			if (thrust != null && thrust.CustomName.Contains(config["Tag"]))
			{
				allThrust.Add(thrust);

				string subtype = thrust.BlockDefinition.SubtypeName;
				if (subtype.Contains("Atmo")) atmoThrust.Add(thrust);
				else if (subtype.Contains("Hydro")) hydroThrust.Add(thrust);
				else if (subtype.Contains("Ion")) ionThrust.Add(thrust);

				Vector3I direction = thrust.GridThrustDirection;
				if (direction == Vector3I.Up) upThrust.Add(thrust);
				else if (direction == Vector3I.Forward) reverseThrust.Add(thrust);

				if (thrust.CustomName.Contains("Aux")) auxThrust.Add(thrust);

				return false;
			}

			IMyTextPanel LCD = block as IMyTextPanel;
			if (LCD != null && LCD.CustomName.Contains(config["LCD Tag"]))
			{
				LCDs.Add(LCD);
				return false;
			}

			return false;
		}

		void SetConfig(IMyTerminalBlock block)
		{
			StringBuilder configstring = new StringBuilder();
			foreach (var keyValue in config) configstring.AppendLine($"{keyValue.Key} = {keyValue.Value}");
			block.CustomData = configstring.ToString();
		}

		void GetConfig(IMyTerminalBlock block)
		{
			string data = block.CustomData;
			string[] lines = data.Split('\n');

			if (lines.Length > 0)
			{
				foreach (string line in lines)
				{
					string[] words = line.Split('=');
					string key = words[0].Trim();
					if (words.Length == 2 && config.ContainsKey(key)) config[key] = words[1].Trim();
				}
			}
			else SetConfig(block);
		}

		//to here
	}
}
