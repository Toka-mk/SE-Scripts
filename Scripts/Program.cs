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

		MyCommandLine _commandLine = new MyCommandLine();
		Dictionary<string, Action> _commands = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase);

		List<IMyShipDrill> drills = new List<IMyShipDrill>();
		List<IMyPistonBase> pistons = new List<IMyPistonBase>();
		IMyPistonBase piston2;
		IMyMotorStator drillRotor;
		IMyMotorStator transformRotor;

		IMyProgrammableBlock drillProg;

		int stage;

		Dictionary<string, string> config;
		Dictionary<string, bool> status;
		string message;

		float pi = (float)Math.PI;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			config = new Dictionary<string, string>
			{
				{"Ship Tag", "[Chroma]"},
				{"Drill Tag", "Drill"},
				{"Cargo Upper Limit", "80"},
				{"Cargo Lower Limit", "40"},
				{"Start Angle", ""},
				{"Ejection", "On"}
			};

			status = new Dictionary<string, bool>
			{
				{"deployed", false},
				{"down", false},
				{"retracting", false},
				{"lowering", false}
			};

			_commands["mode"] = Transform;

			UpdateBlocks();

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
					else Int32.TryParse(words[1], out stage);
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
			savedStatus += "Stage" + "=" + stage;
			Storage = savedStatus;
		}

		public void Main(string arg, UpdateType updateSource)
		{
			if (_commandLine.TryParse(arg))
			{
				Action commandAction;

				string command = _commandLine.Argument(0);

				if (_commands.TryGetValue(_commandLine.Argument(0), out commandAction))
				{
					commandAction();
					message = command;
				}
				else message = $"Unknown command {command}";

				return;
			}

			Echo(message);
		}

		void Ready()
		{
			Retract();
			foreach (IMyPistonBase piston in pistons)
			{
				piston.SetValue<bool>("ShareInertiaTensor", true);
			}
			transformRotor.RotorLock = true;
			transformRotor.SetValue<bool>("ShareInertiaTensor", true);
			drillRotor.RotorLock = true;
			drillRotor.SetValue<bool>("ShareInertiaTensor", true);
		}

		bool Retract()
		{
			drillProg.Enabled = false;
			foreach (IMyPistonBase piston in pistons)
			{
				piston.MaxLimit = 10;
				piston.MinLimit = 0;
				piston.Velocity = -1;
			}
			transformRotor.RotorLock = false;
			transformRotor.TargetVelocityRPM = 2;
			if (!status["down"])
			{
				drillRotor.RotorLock = false;
				drillRotor.TargetVelocityRPM = 2;
			}

			return true;
		}

		void Transform()
		{
			status["down"] = !status["down"];
		}

		bool rotorMoving(IMyMotorStator rotor, bool upper)
		{
			Echo(rotor.CustomName + ": " + rotor.Angle);
			float diff = Math.Abs(NormalizeRad(rotor.Angle - (upper ? rotor.UpperLimitRad : rotor.LowerLimitRad)));
			return diff > 0.01;
		}

		float ToRad(float deg)
		{
			float rad = deg * pi / 180;
			return NormalizeRad(rad);
		}

		float NormalizeRad(float rad)
		{
			if (rad > pi) rad -= (2 * pi);
			if (rad < -pi) rad += (2 * pi);
			return rad;
		}

		void UpdateBlocks()
		{
			GetConfig(Me);

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
		}

		bool CollectBlocks(IMyTerminalBlock block)
		{
			IMyShipDrill drill = block as IMyShipDrill;
			if (drill != null && drill.CustomName.Contains(config["Ship Tag"]))
			{
				drills.Add(drill);
				return false;
			}

			IMyPistonBase piston = block as IMyPistonBase;
			if (piston != null && piston.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				pistons.Add(piston);
				if (piston.CustomName.Contains("Piston 2")) piston2 = piston;
				return false;
			}

			IMyMotorStator rotor = block as IMyMotorStator;
			string name = rotor.CustomName;
			if (rotor != null && name.Contains(config["Ship Tag"]) && name.Contains(config["Drill Tag"]))
			{
				if (rotor.CustomName.Contains("Transform")) transformRotor = rotor;
				else drillRotor = rotor;
			}

			IMyProgrammableBlock prog = block as IMyProgrammableBlock;
			if (prog != null && prog.CustomName == "[Chroma] Drilling Program") drillProg = prog;

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
			lines = lines.Where(x => !string.IsNullOrEmpty(x)).ToArray();

			if (lines.Length != 0)
			{
				foreach (string line in lines)
				{
					string[] words = line.Split('=');
					string key = words[0].Trim();
					if (words.Length == 2 && config.ContainsKey(key)) config[key] = words[1].Trim();
				}

				config["Ship Tag"] += " ";
			}
			else SetConfig(block);
		}


		//to here
	}
}
