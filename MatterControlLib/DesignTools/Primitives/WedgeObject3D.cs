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
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{
	public class WedgeObject3D : PrimitiveObject3D, IPropertyGridModifier, IObjectWithHeight, IObjectWithWidthAndDepth
	{
		public WedgeObject3D()
		{
			Name = "Wedge".Localize();
			Color = Operations.Object3DExtensions.PrimitiveColors["Wedge"];
		}

		public override string ThumbnailName => "Wedge";
	
		public static async Task<WedgeObject3D> Create()
		{
			var item = new WedgeObject3D();

			await item.Rebuild();
			return item;
		}

		[MaxDecimalPlaces(2)]
		public double Width { get; set; } = 20;

		[MaxDecimalPlaces(2)]
		public double Depth { get; set; } = 20;

		[MaxDecimalPlaces(2)]
		public double Height { get; set; } = 20;

		public bool Round { get; set; } = false;

		public int RoundSegments { get; set; } = 15;

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

			RoundSegments = agg_basics.Clamp(RoundSegments, 3, 360 / 4 - 2, ref valuesChanged);

			if (valuesChanged)
			{
				Invalidate(InvalidateType.DisplayValues);
			}

			using (RebuildLock())
			{
				using (new CenterAndHeightMaintainer(this))
				{
					var path = new VertexStorage();
					path.MoveTo(0, 0);
					path.LineTo(Width, 0);

					if (Round)
					{
						var range = 360 / 4.0;
						for (int i = 1; i < RoundSegments - 1; i++)
						{
							var angle = range / (RoundSegments - 1) * i;
							var rad = MathHelper.DegreesToRadians(angle);
							path.LineTo(Width - Math.Sin(rad) * Width, Height - Math.Cos(rad) * Height);
						}
					}

					path.LineTo(0, Height);

					Mesh = VertexSourceToMesh.Extrude(path, Depth);
					Mesh.Transform(Matrix4X4.CreateRotationX(MathHelper.Tau / 4));
				}
			}

			Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Mesh));

			return Task.CompletedTask;
		}

		public void UpdateControls(PublicPropertyChange change)
		{
			if (change.Context.GetEditRow(nameof(RoundSegments)) is GuiWidget segmentsWidget)
			{
				segmentsWidget.Visible = Round;
			}
		}
	}
}