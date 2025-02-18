﻿/*
Copyright (c) 2018, Lars Brubaker, John Lewin
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies,
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.ConfigurationPage.PrintLeveling
{
	public abstract class LevelingPlan
	{
		protected PrinterConfig printer;

		public abstract IEnumerable<Vector2> GetPositionsToSample(Vector3 startingPosition);

		public virtual int ProbeCount { get; }

		public virtual int TotalSteps => this.ProbeCount * 3;

		public LevelingPlan(PrinterConfig printer)
		{
			this.printer = printer;
		}

		public static Vector2 ProbeOffsetSamplePosition(PrinterConfig printer)
		{
			if (printer.Settings.GetValue<bool>(SettingsKey.has_conductive_nozzle)
				&& printer.Settings.GetValue<bool>(SettingsKey.measure_probe_offset_conductively))
			{
				return printer.Settings.GetValue<Vector2>(SettingsKey.conductive_pad_center);
			}

			return printer.Settings.GetValue<Vector2>(SettingsKey.print_center);
		}

		public IEnumerable<Vector2> GetSampleRing(int numberOfSamples, double ratio, double phase)
		{
			double bedRadius = Math.Min(printer.Settings.GetValue<Vector2>(SettingsKey.bed_size).X, printer.Settings.GetValue<Vector2>(SettingsKey.bed_size).Y) / 2;
			Vector2 bedCenter = printer.Settings.GetValue<Vector2>(SettingsKey.print_center);

			for (int i = 0; i < numberOfSamples; i++)
			{
				Vector2 position = new Vector2(bedRadius * ratio, 0);
				position.Rotate(MathHelper.Tau / numberOfSamples * i + phase);
				position += bedCenter;
				yield return position;
			}
		}

		public static bool NeedsToBeRun(PrinterConfig printer)
		{
			PrintLevelingData levelingData = printer.Settings.Helpers.PrintLevelingData;

			var required = printer.Settings.GetValue<bool>(SettingsKey.print_leveling_required_to_print);
			if (required && levelingData == null)
			{
				// need but don't have data
				return true;
			}

			if (printer.Settings.GetValue<bool>(SettingsKey.has_hardware_leveling))
			{
				// If printer has hardware leveling, software leveling is disabled
				return false;
			}

			var enabled = printer.Settings.GetValue<bool>(SettingsKey.print_leveling_enabled);

			if (enabled
				&& printer.Settings.GetValue<bool>(SettingsKey.has_z_probe)
				&& printer.Settings.GetValue<bool>(SettingsKey.use_z_probe)
				&& printer.Settings.GetValue<bool>(SettingsKey.validate_leveling))
			{
				return false;
			}

			if (printer.Settings.Helpers.ValidateLevelingWithProbe)
			{
				// If we are going to validate the leveling before printing we do not need to
				// make the user do it as part of printer calibration.
				return false;
			}

			// check if leveling is turned on
			if (required && !enabled)
			{
				// need but not turned on
				return true;
			}

			if (!required && !enabled)
			{
				return false;
			}

			// check that there are no duplicate points
			var positionCounts = from x in levelingData.SampledPositions
								 group x by x into g
								 let count = g.Count()
								 orderby count descending
								 select new { Value = g.Key, Count = count };

			foreach (var x in positionCounts)
			{
				if (x.Count > 1)
				{
					return true;
				}
			}

			// check that the solution last measured is the currently selected solution
			if (printer.Settings.GetValue<LevelingSystem>(SettingsKey.print_leveling_solution) != levelingData.LevelingSystem)
			{
				return true;
			}

			// check that the bed temperature at probe time was close enough to the current print bed temp
			double requiredLevelingTemp = printer.Settings.GetValue<bool>(SettingsKey.has_heated_bed) ?
				printer.Settings.GetValue<double>(SettingsKey.bed_temperature)
				: 0;

			// check that the number of points sampled is correct for the solution
			switch (levelingData.LevelingSystem)
			{
				case LevelingSystem.Probe3Points:
					if (levelingData.SampledPositions.Count != 3) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe7PointRadial:
					if (levelingData.SampledPositions.Count != 7) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe13PointRadial:
					if (levelingData.SampledPositions.Count != 13) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe100PointRadial:
					if (levelingData.SampledPositions.Count != 100) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe3x3Mesh:
					if (levelingData.SampledPositions.Count != 9) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe5x5Mesh:
					if (levelingData.SampledPositions.Count != 25) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.Probe10x10Mesh:
					if (levelingData.SampledPositions.Count != 100) // different criteria for what is not initialized
					{
						return true;
					}

					break;

				case LevelingSystem.ProbeCustom:
					if (levelingData.SampledPositions.Count != LevelWizardCustom.ParseLevelingSamplePoints(printer).Count)
					{
						return true;
					}

					break;

				default:
					throw new NotImplementedException();
			}

			return false;
		}
	}
}