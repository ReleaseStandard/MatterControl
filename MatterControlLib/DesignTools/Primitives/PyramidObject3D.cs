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
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{
	public class PyramidObject3D : PrimitiveObject3D, IObjectWithHeight, IObjectWithWidthAndDepth
	{
		public PyramidObject3D()
		{
			Name = "Pyramid".Localize();
			Color = Operations.Object3DExtensions.PrimitiveColors["Pyramid"];
		}

		public override string ThumbnailName => "Pyramid";
		
		public static async Task<PyramidObject3D> Create()
		{
			var item = new PyramidObject3D();

			await item.Rebuild();
			return item;
		}

		[MaxDecimalPlaces(2)]
		public double Width { get; set; } = 20;

		[MaxDecimalPlaces(2)]
		public double Depth { get; set; } = 20;

		[MaxDecimalPlaces(2)]
		public double Height { get; set; } = 20;

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
					var path = new VertexStorage();
					path.MoveTo(0, 0);
					path.LineTo(Math.Sqrt(2) * 100, 0);
					path.LineTo(0, Height * 100);

					var mesh = VertexSourceToMesh.Revolve(path, 4);
					mesh.Transform(Matrix4X4.CreateRotationZ(MathHelper.DegreesToRadians(45)) * Matrix4X4.CreateScale(Width / 2 / 100.0, Depth / 2 / 100.0, 1 / 100.0));
					Mesh = mesh;
				}
			}

			Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Mesh));
			return Task.CompletedTask;
		}
	}
}