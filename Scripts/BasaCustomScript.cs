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

		IMyTimerBlock startupTimer;
		IMyPistonBase drillPiston2;

		IMyShipController controller;
		IMyMotorStator stairRotor;
		IMyPistonBase landingPiston;
		IMyMotorStator landingRotor;
		List<IMyLandingGear> landingGears = new List<IMyLandingGear>();
		IMyProgrammableBlock aligner;
		List<IMyDoor> doors = new List<IMyDoor>();
		IMyProgrammableBlock autoDoor;
		IMyAirVent vent;
		bool o2;

		MyPlanetElevation elevation;

		Dictionary<string, string> config;
		Dictionary<string, bool> status;
		string message;

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update10;

			config = new Dictionary<string, string>
			{
				{"Ship Tag", "[Chroma]"},
				{"Controller", "Flight Seat"},
				{"Stair Tag", "Stair"},
				{"Landing Gear Tag", "Landing Gear"},
				{"Aligner", "Aligner"},
				{"Use Surface Elevation", "True"}
			};

			status = new Dictionary<string, bool>
			{
				{"gearDown", true},
				{"locked", true},
				{"aligned", true},
				{"aligner", true}
			};

			_commands["startup"] = Startup;
			_commands["lock"] = Lock;
			_commands["align"] = Align;

			startupTimer = GridTerminalSystem.GetBlockWithName("[Chroma] Startup Timer") as IMyTimerBlock;
			startupTimer.StartCountdown();
			drillPiston2 = GridTerminalSystem.GetBlockWithName("[Chroma] Drill Piston 2") as IMyPistonBase;

			autoDoor = GridTerminalSystem.GetBlockWithName("[Chroma] Auto Door Program") as IMyProgrammableBlock;
			vent = GridTerminalSystem.GetBlockWithName("[Chroma] Air Vent Exterior") as IMyAirVent;

			o2 = vent.GetOxygenLevel() < .6;

			UpdateBlocks();
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

			int alt = GetAltitude();
			bool grav = CheckGrav();

			CheckTask(alt, grav);
			CheckAlign(alt, grav);
			CheckAir();
		}

		void Startup()
		{
			drillPiston2.SetValue<bool>("ShareInertiaTensor", false);
		}

		void CheckAir()
		{
			bool safe = vent.GetOxygenLevel() > 0.6;
			//message = vent.GetOxygenLevel().ToString();

			if (o2 == safe) return;

			foreach (IMyDoor door in doors)
			{
				string name = door.CustomName;
				door.Enabled = true;
				if (name.Contains("R")) door.CustomName = "[Chroma] Door R" + (safe ? "" : " Airlock Exterior");
				else if (name.Contains("L")) door.CustomName = "[Chroma] Door L" + (safe ? "" : " Airlock Exterior");
				else
				{
					door.CustomName = "[Chroma] Door" + (safe ? " Excluded" : " Airlock Interior");
				}
			}
			autoDoor.TryRun("reset");

			o2 = safe;
		}

		void Lock()
		{
			if (CheckGrav()) foreach (IMyLandingGear gear in landingGears) gear.Unlock();
			else
			{
				bool locked = !status["locked"];
				LockGear(locked);
			}
		}

		void Align()
		{
			bool align = !status["aligner"];
			aligner.TryRun(align ? "go" : "stop");
			status["aligner"] = align;
		}

		bool CheckGrav()
		{
			Vector3D grav = controller.GetNaturalGravity();
			return grav.Length() > 1.5;
		}

		void CheckAlign(int alt, bool grav)
		{
			if (grav)
			{
				if (alt > 3000) return;
				if (!status["aligned"])
				{
					aligner.TryRun("go");
					status["aligned"] = true;
					status["alinger"] = true;
				}
			}
			else
			{
				if (status["aligned"])
				{
					aligner.TryRun("stop");
					status["aligned"] = false;
					status["aligner"] = false;
				}
			}
		}

		void CheckTask(int alt, bool grav)
		{
			if (grav)
			{
				if (alt < 30)
				{
					if (!status["gearDown"]) GearDown(true);
				}
				else if (status["gearDown"]) GearDown(false);

				if (alt < 15)
				{
					if (!status["locked"]) LockGear(true);
					foreach (IMyLandingGear gear in landingGears)
					{
						if (gear.IsLocked)
						{
							stairRotor.Torque = 0;
							return;
						}
					}
				}
				else if (status["locked"]) LockGear(false);
			}

			else
			{
				if (status["gearDown"]) GearDown(false);
			}
			
			stairRotor.Torque = 30000000;
		}

		void GearDown(bool down)
		{
			stairRotor.TargetVelocityRPM = down ? -10f : 10f;
			landingRotor.TargetVelocityRPM = down ? 10f : -10f;
			status["gearDown"] = down;
		}

		void LockGear(bool locked)
		{
			landingRotor.RotorLock = locked;
			landingRotor.SetValue<bool>("ShareInertiaTensor", locked);
			landingPiston.SetValue<bool>("ShareInertiaTensor", locked);
			foreach (IMyLandingGear gear in landingGears)
			{
				gear.AutoLock = locked;
				gear.Unlock();
			}
			status["locked"] = locked;
		}

		int GetAltitude()
		{
			double altitude = double.PositiveInfinity;
			controller.TryGetPlanetElevation(elevation, out altitude);
			return (int)altitude;
		}

		void UpdateBlocks()
		{
			GetConfig(Me);
			landingGears.Clear();
			doors.Clear();

			GridTerminalSystem.GetBlocksOfType<IMyTerminalBlock>(null, CollectBlocks);
			if ((config["Use Surface Elevation"] == "True")) elevation = MyPlanetElevation.Surface;
			else elevation = MyPlanetElevation.Sealevel;
		}

		bool CollectBlocks(IMyTerminalBlock block)
		{
			IMyShipController control = block as IMyShipController;
			if (control != null && control.CustomName.Contains(config["Controller"]))
			{
				controller = control;
				return false;
			}

			IMyMotorStator rotor = block as IMyMotorStator;
			if (rotor != null)
			{
				if (rotor.CustomName.Contains(config["Ship Tag"] + config["Stair Tag"])) stairRotor = rotor;
				else if (rotor.CustomName.Contains(config["Landing Gear Tag"])) landingRotor = rotor;
				return false;
			}

			IMyPistonBase piston = block as IMyPistonBase;
			if (piston != null && piston.CustomName.Contains(config["Ship Tag"] + config["Landing Gear Tag"]))
			{
				landingPiston = piston;
				return false;
			}

			IMyLandingGear gear = block as IMyLandingGear;
			if (gear != null && gear.CustomName.Contains(config["Ship Tag"] + config["Landing Gear Tag"]))
			{
				landingGears.Add(gear);
				return false;
			}

			IMyProgrammableBlock align = block as IMyProgrammableBlock;
			if (align != null && align.CustomName.Contains(config["Ship Tag"] + config["Aligner"]))
			{
				aligner = align;
				return false;
			}

			IMyDoor door = block as IMyDoor;
			if (door != null && door.CustomName.Contains(config["Ship Tag"]))
			{
				doors.Add(door);
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
			}
			else SetConfig(block);
		}


		//to here
	}
}
