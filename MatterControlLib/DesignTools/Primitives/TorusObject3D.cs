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
using System.ComponentModel;
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.Plugins.EditorTools;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{
	public class TorusObject3D : PrimitiveObject3D, IPropertyGridModifier, IObject3DControlsProvider
	{
		public TorusObject3D()
		{
			Name = "Torus".Localize();
			Color = Operations.Object3DExtensions.PrimitiveColors["Torus"];
		}

		public override string ThumbnailName => "Torus";

		public static async Task<TorusObject3D> Create()
		{
			var item = new TorusObject3D();
			await item.Rebuild();
			return item;
		}

		[MaxDecimalPlaces(2)]
		public double OuterDiameter { get; set; } = 20;

		[MaxDecimalPlaces(2)]
		public double InnerDiameter { get; set; } = 10;

		public int Sides { get; set; } = 40;

		public bool Advanced { get; set; } = false;

		[ReadOnly(true)]
		[DisplayName("")] // clear the display name so this text will be the full width of the editor
		public string EasyModeMessage { get; set; } = "You can switch to Advanced mode to get more torus options.";

		[MaxDecimalPlaces(2)]
		public double StartingAngle { get; set; } = 0;

		[MaxDecimalPlaces(2)]
		public double EndingAngle { get; set; } = 360;

		public int RingSides { get; set; } = 15;

		[MaxDecimalPlaces(2)]
		public double RingPhaseAngle { get; set; } = 0;

		public override async void OnInvalidate(InvalidateArgs invalidateType)
		{
			if (invalidateType.InvalidateType.HasFlag(InvalidateType.Properties)
				&& invalidateType.Source == this)
			{
				await Rebuild();
			}
			else
			{
				base.OnInvalidate(invalidateType);
			}
		}

		public override Task Rebuild()
		{
			this.DebugDepth("Rebuild");
			bool valuesChanged = false;
			using (RebuildLock())
			{
				InnerDiameter = agg_basics.Clamp(InnerDiameter, 0, OuterDiameter - .1, ref valuesChanged);
				Sides = agg_basics.Clamp(Sides, 3, 360, ref valuesChanged);
				RingSides = agg_basics.Clamp(RingSides, 3, 360, ref valuesChanged);

				StartingAngle = agg_basics.Clamp(StartingAngle, 0, 360 - .01, ref valuesChanged);
				EndingAngle = agg_basics.Clamp(EndingAngle, StartingAngle + .01, 360, ref valuesChanged);

				var ringSides = RingSides;
				var startingAngle = StartingAngle;
				var endingAngle = EndingAngle;
				var ringPhaseAngle = RingPhaseAngle;
				if (!Advanced)
				{
					ringSides = Math.Max(3, (int)(Sides / 2));
					startingAngle = 0;
					endingAngle = 360;
					ringPhaseAngle = 0;
				}

				var innerDiameter = Math.Min(OuterDiameter - .1, InnerDiameter);

				using (new CenterAndHeightMaintainer(this))
				{
					var poleRadius = (OuterDiameter / 2 - innerDiameter / 2) / 2;
					var toroidRadius = innerDiameter / 2 + poleRadius;
					var path = new VertexStorage();
					var angleDelta = MathHelper.Tau / ringSides;
					var ringStartAngle = MathHelper.DegreesToRadians(ringPhaseAngle);
					var ringAngle = ringStartAngle;
					var circleCenter = new Vector2(toroidRadius, 0);
					path.MoveTo(circleCenter + new Vector2(poleRadius * Math.Cos(ringStartAngle), poleRadius * Math.Sin(ringStartAngle)));
					for (int i = 0; i < ringSides - 1; i++)
					{
						ringAngle += angleDelta;
						path.LineTo(circleCenter + new Vector2(poleRadius * Math.Cos(ringAngle), poleRadius * Math.Sin(ringAngle)));
					}

					path.LineTo(circleCenter + new Vector2(poleRadius * Math.Cos(ringStartAngle), poleRadius * Math.Sin(ringStartAngle)));

					var startAngle = MathHelper.Range0ToTau(MathHelper.DegreesToRadians(startingAngle));
					var endAngle = MathHelper.Range0ToTau(MathHelper.DegreesToRadians(endingAngle));
					Mesh = VertexSourceToMesh.Revolve(path, Sides, startAngle, endAngle);
				}
			}

			if (valuesChanged)
			{
				Invalidate(InvalidateType.DisplayValues);
			}

			Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Mesh));
			return Task.CompletedTask;
		}

		public void UpdateControls(PublicPropertyChange change)
		{
			change.SetRowVisible(nameof(StartingAngle), () => Advanced);
			change.SetRowVisible(nameof(EndingAngle), () => Advanced);
			change.SetRowVisible(nameof(RingSides), () => Advanced);
			change.SetRowVisible(nameof(RingPhaseAngle), () => Advanced);
			change.SetRowVisible(nameof(EasyModeMessage), () => !Advanced);
		}

		public void AddObject3DControls(Object3DControlsLayer object3DControlsLayer)
		{
			var getDiameters = new List<Func<double>>() { () => OuterDiameter, () => InnerDiameter };
			var setDiameters = new List<Action<double>>() { (diameter) => OuterDiameter = diameter, (diameter) => InnerDiameter = diameter };
			object3DControlsLayer.Object3DControls.Add(new ScaleDiameterControl(object3DControlsLayer,
				getDiameters,
				setDiameters,
				0,
				ObjectSpace.Placement.Center));
			object3DControlsLayer.Object3DControls.Add(new ScaleDiameterControl(object3DControlsLayer,
				getDiameters,
				setDiameters,
				1,
				ObjectSpace.Placement.Center,
				angleOffset: -MathHelper.Tau / 32));
			object3DControlsLayer.AddControls(ControlTypes.MoveInZ);
			object3DControlsLayer.AddControls(ControlTypes.RotateXYZ);
		}
	}
}