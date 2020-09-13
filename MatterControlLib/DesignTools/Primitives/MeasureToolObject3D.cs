﻿/*
Copyright (c) 2019, Lars Brubaker, John Lewin
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
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MeshVisualizer;
using MatterHackers.PolygonMesh;
using MatterHackers.RenderOpenGl;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{

	public class MeasureToolObject3D : Object3D, IObject3DControlsProvider, IEditorDraw
	{
		public MeasureToolObject3D()
		{
			Name = "Measure Tool".Localize();
			Color = Color.FromHSL(.11, .98, .76);
		}

		public static async Task<MeasureToolObject3D> Create()
		{
			var item = new MeasureToolObject3D();
			await item.Rebuild();
			return item;
		}

		[HideFromEditor]
		public Vector3 StartPosition { get; set; } = new Vector3(-5, 0, 15);

		[HideFromEditor]
		public Vector3 EndPosition { get; set; } = new Vector3(5, 0, 15);

		[ReadOnly(true)]
		public double Distance { get; set; } = 10;

		public List<IObject3DControl> GetObject3DControls(Object3DControlsLayer object3DControlsLayer)
		{
			return new List<IObject3DControl>
			{
				new TracedPositionObject3DControl(object3DControlsLayer, this, () => StartPosition, (position) =>
				{
					StartPosition = position;
					Distance = (StartPosition - EndPosition).Length;
				}),
				new TracedPositionObject3DControl(object3DControlsLayer, this, () => EndPosition, (position) =>
				{
					EndPosition = position;
					Distance = (StartPosition - EndPosition).Length;
				}),
			};
		}

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

			using (RebuildLock())
			{
				using (new CenterAndHeightMaintainer(this))
				{
					Mesh = PlatonicSolids.CreateCube(20, 20, 10);
				}
			}

			Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Mesh));
			return Task.CompletedTask;
		}

		public void DrawEditor(Object3DControlsLayer object3DControlLayer, List<Object3DView> transparentMeshes, DrawEventArgs e, ref bool suppressNormalDraw)
		{
			object3DControlLayer.World.Render3DLine(StartPosition.Transform(Matrix), EndPosition.Transform(Matrix), Color.Black, width: GuiWidget.DeviceScale);
		}
	}
}