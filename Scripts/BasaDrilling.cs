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
		List<IMyCargoContainer> cargos = new List<IMyCargoContainer>();

		IMyProgrammableBlock drillProg;
		IMyCockpit seat;

		int stage;
		int upper;
		int lower;

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
			};

			status = new Dictionary<string, bool>
			{
				{"deployed", false},
				{"down", false},
				{"retracting", false},
				{"sleep", false},
				{"pause", false},
				{"autopause", false}
			};

			_commands["mode"] = Transform;
			_commands["reset"] = Reset;
			_commands["deploy"] = Deploy;
			_commands["start"] = Start;
			_commands["pause"] = Pause;

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

			seat.GetSurface(2).ContentType = ContentType.TEXT_AND_IMAGE;
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
					message = command + "\n" + message;
				}
				else message = $"Unknown command {command}";
			}

			message = "Drilling Mode:\n" + (status["down"] ? "Downward" : "Forward")
					+ "\n\nDrill Deployed:\n" + status["deployed"];

			message += "\n\nStage: " + stage + "\n";
			foreach (var keyValue in status) message += keyValue.Key + ": " + keyValue.Value + "\n";

			CheckCargo();
			Retract();
			Drilling();

			Echo(message);
			Echo("\nExecution Time: " + Runtime.LastRunTimeMs.ToString() + "ms");
			
			IMyTextSurface surface = seat.GetSurface(2);
			surface.FontColor = status["deployed"] ? Color.DodgerBlue : Color.Yellow;
			surface.WriteText(message);

		}

		void CheckCargo()
		{
			if (!status["deployed"] || stage == 1 || upper == 0f || lower == 0f) return;

			float current = 0;
			float max = 0;
			foreach (IMyCargoContainer cargo in cargos)
			{
				IMyInventory inv = cargo.GetInventory(0);
				current += (float)inv.CurrentVolume;
				max += (float)inv.MaxVolume;
			}
			if (max == 0) return;

			float fillRate = 100 * current / max;

			//message += current + "\n" + max + "\n" + fillRate + "\n" + upper + "\n" + lower;

			if (fillRate > upper)
			{
				status["autopause"] = true;
				Pause();
			}
			else if (fillRate <= lower && status["autopause"])
			{
				status["autopause"] = false;
				Start();
			}
		}

		void Transform()
		{
			status["down"] = !status["down"];
		}

		void Start()
		{
			UpdateBlocks();
			if (status["pause"] && stage != 0) stage = Math.Abs(stage);
			else stage = status["down"] ? 1 : 20;
			status["sleep"] = false;
			status["autopause"] = false;
		}

		void Deploy()
		{
			if (status["deployed"]) Reset();
			status["deployed"] = true;
			piston2.Enabled = true;
			piston2.MaxLimit = 10;
			piston2.MinLimit = 0;
			piston2.Velocity = 2;
		}

		void Reset()
		{
			status["retracting"] = true;
		}

		void Pause()
		{
			status["pause"] = true;
			status["sleep"] = true;
			stage = -Math.Abs(stage);
			if (status["down"])
			{
				foreach (IMyPistonBase piston in pistons) piston.Enabled = false;
				foreach (IMyShipDrill drill in drills) drill.Enabled = false;
				transformRotor.Enabled = false;
			}
			else drillProg.TryRun("pause");
		}

		void Drilling()
		{
			if (stage < 1 || status["retracting"] || status["sleep"]) return;

			if (status["pause"])
			{
				if (status["down"])
				{
					foreach (IMyPistonBase piston in pistons) piston.Enabled = true;
					foreach (IMyShipDrill drill in drills) drill.Enabled = true;
					transformRotor.Enabled = true;
				}
				else
				{
					drillProg.Enabled = true;
					drillProg.TryRun("start");
				}
				status["pause"] = false;
				status["sleep"] = false;
				return;
			}

			status["deployed"] = true;

			switch (stage)
			{
				case 1:
					foreach (IMyShipDrill drill in drills) drill.Enabled = true;
					transformRotor.RotorLock = false;
					transformRotor.LowerLimitDeg = 235;
					transformRotor.TargetVelocityRPM = -1;
					stage = 2;
					break;

				case 2:
					if (rotorMoving(transformRotor, false)) return;
					stage = 3;
					break;

				case 3:
					foreach (IMyPistonBase piston in pistons)
					{
						piston.Enabled = true;
						piston.MaxLimit = 10;
						piston.MinLimit = 3;
						piston.Velocity = .2f;
					}
					stage = 4;
					break;

				case 4:
					foreach (IMyPistonBase piston in pistons) if (piston.CurrentPosition != 10) return;
					stage = 5;
					break;

				case 5:
					transformRotor.LowerLimitDeg = 180;
					transformRotor.TargetVelocityRPM = -.5f;
					stage = 6;
					break;

				case 6:
					if (rotorMoving(transformRotor, false)) return;
					stage = 7;
					break;

				case 7:
					foreach (IMyPistonBase piston in pistons) piston.Velocity = -.15f;
					stage = 8;
					break;

				case 8:
					foreach (IMyPistonBase piston in pistons) if (piston.CurrentPosition != piston.MinLimit) return;
					stage = 9;
					break;

				case 9:
					transformRotor.LowerLimitDeg = 130;
					stage = 10;
					break;

				case 10:
					if (rotorMoving(transformRotor, false)) return;
					stage = 11;
					break;

				case 11:
					foreach (IMyPistonBase piston in pistons)
					{
						piston.MinLimit = 0;
						piston.Velocity = -.2f;
					}
					stage = 12;
					break;

				case 12:
					foreach (IMyPistonBase piston in pistons) if (piston.CurrentPosition != 0) return;
					stage = 13;
					break;

				case 13:
					transformRotor.UpperLimitDeg = 180;
					transformRotor.TargetVelocityRPM = .5f;
					stage = 14;
					break;

				case 14:
					if (rotorMoving(transformRotor, true)) return;
					stage = 15;
					break;

				case 15:
					foreach (IMyPistonBase piston in pistons)
					{
						piston.MaxLimit = 1.5f;
						piston.Velocity = .15f;
					}
					stage = 16;
					break;

				case 16:
					foreach (IMyPistonBase piston in pistons) if (piston.CurrentPosition != piston.MaxLimit) return;
					Reset();
					break;

				case 20:
					drillProg.Enabled = true;
					drillRotor.RotorLock = false;
					drillProg.TryRun("set");
					stage = 21;
					break;

				case 21:
					if (!rotorMoving(drillRotor)) stage = 22;
					else return;
					break;

				case 22:
					drillProg.TryRun("start");
					status["sleep"] = true;
					break;
			}

		}

		void Retract()
		{
			if (!status["retracting"]) return;

			if (stage != 0) stage = 0;

			if (status["deployed"])
			{
				drillProg.Enabled = false;
				foreach (IMyPistonBase piston in pistons)
				{
					piston.Enabled = true;
					piston.MaxLimit = 10;
					piston.MinLimit = 0;
					piston.Velocity = -1;
					piston.SetValue<bool>("ShareInertiaTensor", true);
				}
				piston2.SetValue<bool>("ShareInertiaTensor", false);
				transformRotor.Enabled = true;
				transformRotor.LowerLimitDeg = 180;
				transformRotor.UpperLimitDeg = 270;
				transformRotor.RotorLock = false;
				transformRotor.TargetVelocityRPM = 2;
				transformRotor.SetValue<bool>("ShareInertiaTensor", false);
				if (!status["down"])
				{
					drillRotor.Enabled = true;
					drillRotor.RotorLock = false;
					drillRotor.TargetVelocityRPM = -3;
					drillRotor.SetValue<bool>("ShareInertiaTensor", false);
				}

				status["deployed"] = false;
				status["retracting"] = true;

				return;
			}
			else if (rotorMoving(transformRotor, true) || rotorMoving(drillRotor))
			{
				return;
			}

			foreach (IMyShipDrill drill in drills) drill.Enabled = false;
			transformRotor.RotorLock = true;
			transformRotor.SetValue<bool>("ShareInertiaTensor", true);
			drillRotor.RotorLock = true;
			drillRotor.SetValue<bool>("ShareInertiaTensor", true);
			status["retracting"] = false;
			return;
		}

		bool rotorMoving(IMyMotorStator rotor, bool upper)
		{
			float diff = Math.Abs(NormalizeRad(rotor.Angle - (upper ? rotor.UpperLimitRad : rotor.LowerLimitRad)));
			return diff > 0.03;
		}

		bool rotorMoving(IMyMotorStator rotor)
		{
			float diff = Math.Abs(NormalizeRad(rotor.Angle - rotor.UpperLimitRad));
			if (diff < 0.03) return false;

			diff = Math.Abs(NormalizeRad(rotor.Angle - rotor.LowerLimitRad));
			if (diff < 0.03) return false;

			return true;
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
			if (rotor != null)
			{
				string name = rotor.CustomName;
				if (!name.Contains(config["Ship Tag"]) || !name.Contains(config["Drill Tag"])) return false;
				else if (rotor.CustomName.Contains("Transform")) transformRotor = rotor;
				else drillRotor = rotor;
			}

			IMyProgrammableBlock prog = block as IMyProgrammableBlock;
			if (prog != null && prog.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				drillProg = prog;
				return false;
			}

			IMyCockpit cockpit = block as IMyCockpit;
			if (cockpit != null && cockpit.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				seat = cockpit;
				return false;
			}

			IMyCargoContainer cargo = block as IMyCargoContainer;
			if (cargo != null && cargo.CustomName.Contains(config["Ship Tag"] + config["Drill Tag"]))
			{
				cargos.Add(cargo);
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
				Int32.TryParse(config["Cargo Upper Limit"], out upper);
				Int32.TryParse(config["Cargo Lower Limit"], out lower);
			}
			else SetConfig(block);
		}


		//to here
	}
}




