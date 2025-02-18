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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using MatterHackers.Agg.Font;
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{
	public class RibbonWithNameObject3D : Object3D
	{
		public RibbonWithNameObject3D()
		{
			Rebuild();
		}

		[DisplayName("Name")]
		public string NameToWrite { get; set; } = "MatterHackers";

		public NamedTypeFace Font { get; set; } = new NamedTypeFace();

		public override Task Rebuild()
		{
			IObject3D cancerRibbonStl = Object3D.Load("Cancer_Ribbon.stl", CancellationToken.None);

			cancerRibbonStl = new RotateObject3D(cancerRibbonStl, MathHelper.DegreesToRadians(90));

			var letterPrinter = new TypeFacePrinter(NameToWrite.ToUpper(), new StyledTypeFace(ApplicationController.GetTypeFace(Font), 12));

			IObject3D nameMesh = new Object3D()
			{
				Mesh = VertexSourceToMesh.Extrude(letterPrinter, 5)
			};

			AxisAlignedBoundingBox textBounds = nameMesh.GetAxisAlignedBoundingBox();
			var textArea = new Vector2(25, 6);

			double scale = Math.Min(textArea.X / textBounds.XSize, textArea.Y / textBounds.YSize);
			nameMesh = new ScaleObject3D_3(nameMesh, scale, scale, 2 / textBounds.ZSize);
			nameMesh = new AlignObject3D(nameMesh, FaceAlign.Bottom | FaceAlign.Front, cancerRibbonStl, FaceAlign.Top | FaceAlign.Front, 0, 0, -1);
			nameMesh = new SetCenterObject3D(nameMesh, cancerRibbonStl.GetCenter(), true, false, false);

			nameMesh = new RotateObject3D(nameMesh, 0, 0, MathHelper.DegreesToRadians(50));
			nameMesh = new TranslateObject3D(nameMesh, -37, -14, -1);

			// output two meshes for card holder and text
			this.Children.Modify(list =>
			{
				list.Clear();
				list.Add(cancerRibbonStl);
				list.Add(nameMesh);
			});

			this.Mesh = null;
			this.Invalidate(InvalidateType.Children);
			return Task.CompletedTask;
		}
	}
}