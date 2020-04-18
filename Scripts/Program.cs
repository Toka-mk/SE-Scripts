﻿using Sandbox.Game.EntityComponents;
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
		List<IMyPistonBase> pistonsX = new List<IMyPistonBase>();
		List<IMyPistonBase> pistonsY = new List<IMyPistonBase>();
		List<IMyPistonBase> pistonsZ = new List<IMyPistonBase>();
		IMyMotorStator drillRotor;
		IMyTimerBlock rotorLock;
		IMyTimerBlock unlockT;
		IMyTimerBlock unlockB;
		IMyTimerBlock lockT;
		IMyTimerBlock lockB;
		IMyProjector projector;
		IMyShipWelder welder;
		IMyCargoContainer cargo;
		IMyConveyorSorter drillSorter;
		IMyPistonBase pistonB;
		IMyPistonBase pistonT;

		IMyTextSurface LCD;

		IMyShipMergeBlock mergeT;
		IMyShipMergeBlock mergeB;
		//IMyShipMergeBlock mergeR;

		int stage;
		int rStage;
		int lStage;
		int depth;
		int targetDepth;
		//int level;
		bool inner;
		float rotorVO;
		float rotorVI;
		float speed;

		bool pause;
		List<MyInventoryItem> items = new List<MyInventoryItem>();
		Dictionary<string, string> config;
		Dictionary<string, int> status;
		string message;
		string error = "";

		float pi = (float)Math.PI;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			config = new Dictionary<string, string>
			{
				{"Tag", "[Digger]"},
				{"Target Depth", "90"},
				{"Speed", ".5"},
				{"Cargo Name", "[Digger] Cargo"}
			};

			status = new Dictionary<string, int>
			{
				{"stage", 1},
				{"rStage", 1},
				{"lStage", 0},
				{"depth", 0},
				{"pause", 0}
			};

			_commands["start"] = Start;
			_commands["pause"] = Pause;
			_commands["reset"] = Reset;
			_commands["refresh"] = GetBlocks;

			GetBlocks();

			string[] savedStatus = Storage.Split('\n');
			if (savedStatus.Length > 0)
			{
				foreach (string line in savedStatus)
				{
					string[] words = line.Split('=');
					if (words.Length != 2) continue;
					string key = words[0];
					if (status.ContainsKey(key))
					{
						int value;
						Int32.TryParse(words[1], out value);
						status[key] = value;
					}
				}

				stage = status["stage"];
				lStage = status["lStage"];
				rStage = status["rStage"];
				depth = status["depth"];
				pause = status["pause"] == 1;
			}
		}

		public void Save()
		{
			status["stage"] = stage;
			status["lStage"] = lStage;
			status["rStage"] = rStage;
			status["depth"] = depth;
			status["pause"] = pause ? 1 : 0;
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

			message = "Stage: " + stage + "\n";
			message += "Current Depth: " + depth + "\n" + "Target Depth: " + targetDepth + "\n";
			//foreach (var keyValue in status) message += keyValue.Key + ": " + keyValue.Value + "\n";

			Dig();
			NewLevel();
			Cargo();

			message += "\nExecution Time: " + Runtime.LastRunTimeMs.ToString() + "ms";
			LCD.WriteText(error + message);

		}

		void Pause()
		{
			foreach (IMyShipDrill drill in drills) drill.Enabled = false;
			foreach (IMyPistonBase piston in pistonsX) piston.Enabled = false;
			foreach (IMyPistonBase piston in pistonsY) piston.Enabled = false;
			foreach (IMyPistonBase piston in pistonsZ) piston.Enabled = false;
			drillRotor.Enabled = false;
			projector.Enabled = false;
			welder.Enabled = false;
			pause = true;
		}

		void Start()
		{
			error = "";
			if (pause)
			{
				pause = false;
			}
			else
			{
				stage = 1;
				rStage = 1;
				lStage = 0;
				if (mergeB.CubeGrid == mergeT.CubeGrid) unlockB.Trigger();
				GetConfig(Me);
			}

			foreach (IMyShipDrill drill in drills) drill.Enabled = true;
			foreach (IMyPistonBase piston in pistonsX) piston.Enabled = true;
			foreach (IMyPistonBase piston in pistonsY) piston.Enabled = true;
			foreach (IMyPistonBase piston in pistonsZ) piston.Enabled = true;
			drillRotor.Enabled = false;
			projector.Enabled = true;
			welder.Enabled = true;
		}

		void Reset()
		{
			foreach (IMyPistonBase piston in pistonsX) piston.Velocity = -2f;
			foreach (IMyPistonBase piston in pistonsY) piston.Velocity = piston.CustomName.Contains("Inv") ? 1 : -1;
			foreach (IMyShipDrill drill in drills) drill.Enabled = false;
			drillRotor.RotorLock = true;
			drillRotor.LowerLimitDeg = 0;
			drillRotor.TargetVelocityRPM = -2;
			rotorLock.StartCountdown();
			stage = 0;
			rStage = 0;
			lStage = 0;
		}

		void Cargo()
		{
			IMyInventory inv = cargo.GetInventory();

			if (((float)inv.CurrentVolume / (float)inv.MaxVolume) > 0.8)
			{
				drillSorter.Enabled = false;
				return;
			}

			items.Clear();
			inv.GetItems(items);
			int count = 0;

			foreach (var item in items)
			{
				string name = item.Type.ToString();

				if (name.Contains("Ingot/Iron") || name.Contains("Ingot/Silicon") || name.Contains("Ingot/Nickel"))
				{
					count++;
					if (item.Amount < 1000)
					{
						drillSorter.Enabled = true;
						return;
					}
				}
			}

			if (count < 3) drillSorter.Enabled = true;
		}

		void Dig()
		{
			if (stage < 1 || depth >= targetDepth || pause) return;

			switch (stage)
			{
				case 1:
					foreach (IMyPistonBase piston in pistonsZ)
					{
						unlockB.Trigger();
						piston.MaxLimit = piston.CurrentPosition + 1.5f;
						if (piston.MaxLimit >= 9.85f)
						{
							piston.MaxLimit = 9.85f;
							lStage = 1;
						}
						piston.Velocity = speed / pistonsZ.Count();
					}
					foreach (IMyPistonBase piston in pistonsX) piston.Velocity = speed / pistonsX.Count();
					inner = false;
					foreach (IMyShipDrill drill in drills) drill.Enabled = true;
					stage++;
					break;

				case 2:
				case 3:
					if (!DigRoutine()) return;
					stage++;
					break;
					
				case 4:
					foreach (IMyPistonBase piston in pistonsX) piston.Velocity = -speed / pistonsX.Count();
					inner = true;
					stage++;
					break;

				case 5:
				case 6:
					if (!DigRoutine()) return;
					stage++;
					break;

				case 7:
					inner = false;
					stage = 1;
					depth += 3;
					if (depth >= targetDepth) Pause();
					break;

			}
		}

		bool DigRoutine()
		{
			switch (rStage)
			{
				case 1:
					if (PistonMoving(pistonsX[0]) || PistonMoving(pistonsY[0]) || PistonMoving(pistonsZ[0])) return false;
					SetDrillRotor();
					rStage++;
					return false;

				case 2:
					if (RotorMoving(drillRotor)) return false;
					drillRotor.RotorLock = true;
					foreach (IMyPistonBase piston in pistonsY) piston.Velocity = speed / pistonsY.Count() * (piston.Velocity > 0 ? -1 : 1);
					rStage++;
					return false;

				default:
					if (PistonMoving(pistonsY[0])) return false;
					rStage = 1;
					return true;
			}
		}

		void NewLevel()
		{
			if (lStage < 1 || pause) return;

			switch (lStage)
			{
				case 1:
					if (pistonsZ[0].CurrentPosition != 9.85f) return;
					projector.Enabled = false;
					welder.Enabled = false;
					lockB.Trigger();
					lStage++;
					break;
				
				case 2:
					if (pistonB.CurrentPosition != pistonB.MaxLimit) return;
					if (mergeB.CubeGrid != mergeT.CubeGrid)
					{
						error = "ERROR:\nMERGE BLOCK NOT CONNECTED\n";
						//Pause();
						return;
					}
					//level++;
					//mergeR = GridTerminalSystem.GetBlockWithName("[Digger] Rail Merge Block") as IMyShipMergeBlock;
					//mergeR.CustomName += " " + level;
					unlockT.StartCountdown();
					foreach (IMyPistonBase piston in pistonsZ) piston.Velocity = 1;
					lStage++;
					break;

				case 3:
					if (pistonsZ[0].CurrentPosition != 2.35f) return;
					lockT.Trigger();
					lStage++;
					break;

				case 4:
					if (pistonT.CurrentPosition != pistonT.MaxLimit) return;
					if (mergeB.CubeGrid != mergeT.CubeGrid)
					{
						error = "ERROR:\nMERGE BLOCK NOT CONNECTED\n";
						//Pause();
						return;
					}
					unlockB.StartCountdown();
					projector.Enabled = true;
					welder.Enabled = true;
					lStage = 0;
					break;
			}
		}

		void SetDrillRotor()
		{
			drillRotor.LowerLimitDeg = 180f - drillRotor.LowerLimitDeg;
			drillRotor.UpperLimitDeg = float.MaxValue;
			drillRotor.TargetVelocityRPM = inner ? -rotorVI : -rotorVO;
			drillRotor.RotorLock = true;
			rotorLock.StartCountdown();
		}

		bool RotorMoving(IMyMotorStator rotor)
		{
			if (rotor.TargetVelocityRPM == 0) return false;
			float diff = rotor.Angle * 180 / pi;
			//message += rotor.CustomName + ": " + diff + "\n";
			diff -= (rotor.TargetVelocityRPM > 0 ? rotor.UpperLimitDeg : rotor.LowerLimitDeg);
			diff = Math.Abs(Normalize(diff));
			//message += diff + "\n";
			//message += diff > 0.5f;
			return diff > 0.5f;
		}

		float Normalize(float angle, bool rad=false)
		{
			float upper = rad ? pi : 180;
			float lower = rad ? -pi : -180;
			float step = rad ? 2 * pi : 360;
			while (angle >= upper) angle -= step;
			while (angle <= lower) angle += step;
			return angle;
		}

		bool PistonMoving(IMyPistonBase piston)
		{
			if (piston.Velocity == 0) return false;
			//message += piston.CustomName + piston.CurrentPosition + (piston.Velocity > 0 ? piston.MaxLimit : piston.MinLimit) + "\n";
			return piston.CurrentPosition != (piston.Velocity > 0 ? piston.MaxLimit : piston.MinLimit);
		}

		public void GetBlocks()
		{
			GetConfig(Me);
			drills.Clear();
			pistonsX.Clear();
			pistonsY.Clear();
			pistonsZ.Clear();

			IMyBlockGroup group = GridTerminalSystem.GetBlockGroupWithName("[Digger] Drills");
			group.GetBlocksOfType(drills);

			group = GridTerminalSystem.GetBlockGroupWithName("[Digger] Pistons X");
			group.GetBlocksOfType(pistonsX);

			group = GridTerminalSystem.GetBlockGroupWithName("[Digger] Pistons Y");
			group.GetBlocksOfType(pistonsY);

			group = GridTerminalSystem.GetBlockGroupWithName("[Digger] Pistons Z");
			group.GetBlocksOfType(pistonsZ);

			cargo = GridTerminalSystem.GetBlockWithName(config["Cargo Name"]) as IMyCargoContainer;
			drillSorter = GridTerminalSystem.GetBlockWithName("[Digger] Drill Sorter") as IMyConveyorSorter;

			drillRotor = GridTerminalSystem.GetBlockWithName("[Digger] Drill Rotor") as IMyMotorStator;
			rotorLock = GridTerminalSystem.GetBlockWithName("[Digger] Rotor Lock Timer") as IMyTimerBlock;
			unlockB = GridTerminalSystem.GetBlockWithName("[Digger] Platform B Unlock Timer") as IMyTimerBlock;
			lockB = GridTerminalSystem.GetBlockWithName("[Digger] Platform B Lock Timer") as IMyTimerBlock;
			unlockT = GridTerminalSystem.GetBlockWithName("[Digger] Platform T Unlock Timer") as IMyTimerBlock;
			lockT = GridTerminalSystem.GetBlockWithName("[Digger] Platform T Lock Timer") as IMyTimerBlock;
			//LCD = GridTerminalSystem.GetBlockWithName("[Digger] Debug LCD") as IMyTextSurface;
			LCD = Me.GetSurface(0) as IMyTextSurface;
			projector = GridTerminalSystem.GetBlockWithName("[Digger] Rail Projector") as IMyProjector;
			welder = GridTerminalSystem.GetBlockWithName("[Digger] Platform Welder") as IMyShipWelder;
			mergeT = GridTerminalSystem.GetBlockWithName("[Digger] Platform Merge Block T") as IMyShipMergeBlock;
			mergeB = GridTerminalSystem.GetBlockWithName("[Digger] Platform Merge Block B") as IMyShipMergeBlock;
			pistonT = GridTerminalSystem.GetBlockWithName("[Digger] Platform Piston T") as IMyPistonBase;
			pistonB = GridTerminalSystem.GetBlockWithName("[Digger] Platform Piston B") as IMyPistonBase;
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

				config["Tag"] += " ";
				Int32.TryParse(config["Target Depth"], out targetDepth);
				speed = (float)Convert.ToDouble(config["Speed"]);
				rotorVI = 60 * speed / 2 / pi / (float)((drills.Count() -1) * 2.5);
				rotorVO = 60 * speed / 2 / pi / (float)((drills.Count() * 2 - 1) * 2.5);
			}
			else SetConfig(block);
		}


		//to here
	}
}




