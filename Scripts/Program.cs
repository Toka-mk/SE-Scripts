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

		List<IMyPistonBase> pistons = new List<IMyPistonBase>();
		List<IMyShipDrill> drills = new List<IMyShipDrill>();
		IMyMotorStator	drillRotor,
						deployRotor1,
						deployRotor2,
						doorRotorR,
						doorRotorL;

		//List<IMyCargoContainer> cargos = new List<IMyCargoContainer>();

		//IMyProgrammableBlock drillProg;

		Dictionary<string, string> config;
		Dictionary<string, bool> status;
		string message;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			config = new Dictionary<string, string>
			{
				{"Ship Tag", ""},
				{"Drill Tag", "Drill"}
			};

			status = new Dictionary<string, bool>
			{
				{"deployed", false},
				{"drilling", false},
				{"moving", false}
			};

			_commands["deploy"] = Deploy;

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
				}
			}

			Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
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
			if (_commandLine.TryParse(arg))
			{
				Action commandAction;

				string command = _commandLine.Argument(0);

				if (_commands.TryGetValue(_commandLine.Argument(0), out commandAction))
				{
					commandAction();
					message = command + "\n" + message;
				}
				else message = $"Unknown command {command}";
			}

			message = "Drill Deployed:\n" + status["deployed"];

			CheckDeploy();

			Echo(message);
			Echo("\nExecution Time: " + Runtime.LastRunTimeMs.ToString() + "ms");

			Me.GetSurface(0).WriteText(message);

		}

		void Deploy()
		{
			status["deployed"] = !status["deployed"];
			Me.GetSurface(0).FontColor = status["deployed"] ? Color.DodgerBlue : Color.Yellow;
			status["moving"] = true;
		}

		void CheckDeploy()
		{
			if (!status["moving"]) return;
			int drL, dr1, dr2, dr;
			if (status["deployed"])
			{
				drL = RotorStatus(doorRotorL);
				if (drL == 2)
				{
					drillRotor.RotorLock = true;
					deployRotor1.RotorLock = true;
					deployRotor2.RotorLock = true;
					doorRotorL.TargetVelocityRPM = -5;
					doorRotorR.TargetVelocityRPM = 5;
				}
				else if (drL == 1) 
				{
					dr1 = RotorStatus(deployRotor1);
					if (dr1 == 2)
					{
						deployRotor1.RotorLock = false;
						deployRotor1.TargetVelocityRPM = -3;
					}
					else if (dr1 == 1)
					{
						dr = RotorStatus(drillRotor);
						if (dr == 1)
						{
							deployRotor1.RotorLock = true;
							drillRotor.RotorLock = false;
							drillRotor.TargetVelocityRPM = 5;
						}
						else if (dr == 2)
						{
							dr2 = RotorStatus(deployRotor2);
							if (dr2 == 2)
							{
								deployRotor2.RotorLock = false;
								deployRotor2.TargetVelocityRPM = -5;
								foreach (IMyShipDrill drill in drills) drill.Enabled = true;
							}
							else if(dr2 == 1)
							{
								deployRotor2.RotorLock = true;
								status["moving"] = false;
							}
						}
					}
				}
			}
			else
			{
				dr = RotorStatus(drillRotor);
				dr2 = RotorStatus(deployRotor2);
				if (drillRotor.UpperLimitDeg != 90f)
				{
					
					drillRotor.UpperLimitDeg = 90f;
					drillRotor.LowerLimitDeg = 0f;
					drillRotor.TargetVelocityRPM = 5;
					foreach (IMyShipDrill drill in drills) drill.Enabled = false;
					foreach (IMyPistonBase piston in pistons) piston.Velocity = -1;
				}
				else if (dr == 2)
				{
					dr2 = RotorStatus(deployRotor2);
					if (dr2 == 1)
					{
						deployRotor2.RotorLock = false;
						deployRotor2.TargetVelocityRPM = 5;
					}
					else if (dr2 == 2)
					{
						dr = RotorStatus(drillRotor);
						if (dr == 2)
						{
							deployRotor2.RotorLock = true;
							drillRotor.TargetVelocityRPM = -5;
						}
					}
				}
				else if (dr == 1 && dr2 == 2)
				{
					dr1 = RotorStatus(deployRotor1);
					if (dr1 == 1 && pistons[0].CurrentPosition == 0)
					{
						drillRotor.RotorLock = true;
						deployRotor1.RotorLock = false;
						deployRotor1.TargetVelocityRPM = 3;
					}
					else if (dr1 == 2)
					{
						deployRotor1.RotorLock = true;
						doorRotorL.TargetVelocityRPM = 5;
						doorRotorR.TargetVelocityRPM = -5;
						status["moving"] = false;
					}
				}
			}
		}

		int RotorStatus(IMyMotorStator rotor, float rotorThreashold = .03f)
		{
			float diff = Math.Abs(Normalize(rotor.Angle - rotor.UpperLimitRad, true));
			if (diff < rotorThreashold) return 2;

			diff = Math.Abs(Normalize(rotor.Angle - rotor.LowerLimitRad, true));
			if (diff < rotorThreashold) return 1;

			return 0;
		}

		float Normalize(float angle, bool rad)
		{
			float c = rad ? (float)Math.PI : 180;
			while (angle > c) angle -= (2 * c);
			while (angle < -c) angle += (2 * c);
			return angle;
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

			IMyMotorStator rotor = block as IMyMotorStator;
			if (rotor != null)
			{
				string name = rotor.CustomName;
				if (!name.Contains(config["Ship Tag"]) || !name.Contains(config["Drill Tag"])) return false;
				else if (rotor.CustomName.Contains("Deploy Rotor 1")) deployRotor1 = rotor;
				else if (rotor.CustomName.Contains("Deploy Rotor 2")) deployRotor2 = rotor;
				else if (rotor.CustomName.Contains("Door Rotor L")) doorRotorL = rotor;
				else if (rotor.CustomName.Contains("Door Rotor R")) doorRotorR = rotor;
				else drillRotor = rotor;
				return false;
			}

			IMyPistonBase piston = block as IMyPistonBase;
			if (piston != null && piston.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				pistons.Add(piston);
				return false;
			}

			/*
			IMyProgrammableBlock prog = block as IMyProgrammableBlock;
			if (prog != null && prog.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				drillProg = prog;
				return false;
			}
			*/
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




