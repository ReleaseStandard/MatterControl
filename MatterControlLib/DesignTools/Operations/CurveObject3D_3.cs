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
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.DataConverters3D;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.PolygonMesh;
using MatterHackers.RenderOpenGl;
using MatterHackers.RenderOpenGl.OpenGl;
using MatterHackers.VectorMath;

namespace MatterHackers.MatterControl.DesignTools
{
	public class CurveObject3D_3 : OperationSourceContainerObject3D, IPropertyGridModifier, ISelectedEditorDraw
	{
		public CurveObject3D_3()
		{
			Name = "Curve".Localize();
		}

		public enum BendDirections
		{
			Bend_Up,
			Bend_Down,
		}

		public enum BendTypes
		{
			[Description("Bend the part by an angle")]
			Angle,
			[Description("Bend the part around a specified diameter")]
			Diameter,
		}

		[EnumDisplay(Mode = EnumDisplayAttribute.PresentationMode.Tabs)]
		public BendTypes BendType { get; set; } = BendTypes.Angle;

		
		[MaxDecimalPlaces(2)]
		[Description("Set the radius that the bend will wrap around")]
		[DescriptionImage("https://lh3.googleusercontent.com/PpQKIIOqD-49UMhgM_HCvig9Mw_UtUwO08UoRVSLJlCv9h5cGBLMvaXbtORrVQrWYPcKZ4_DfrDoKfcu2TuyYVQOl3AeZNoYflgnijc")]
		public double Diameter { get; set; } = double.MaxValue;

		[MaxDecimalPlaces(1)]
		[Description("Set the angle of the curvature")]
		[DescriptionImage("https://lh3.googleusercontent.com/TYe-CZfwJMKvP2JWBQihkvHD1PyB_nvyf0h3DhvyJu1RBjQWgqeOEsSH3sYcwA4alJjJmziueYGCbB_mic_QoYKuhKrmipkV2eG4_A")]
		public double Angle { get; set; } = 90;

		[EnumDisplay(Mode = EnumDisplayAttribute.PresentationMode.Buttons)]
		[Description("The part will bend around the z axis either up or down")]
		[DescriptionImage("https://lh3.googleusercontent.com/h-s2FyBKO5etYDr_9YSLtGmGmQTcmSGMu4p0mRqX4_7Z62Ndn2QRLoFICC6X9scbhr1EP29RiYRj4EmhLMUwiNTAG-PIiFbzI_jAses")]		
		public BendDirections BendDirection { get; set; } = BendDirections.Bend_Up;

		[Range(0, 100, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
		[Description("Where to start the bend as a percent from the left side")]
		[DescriptionImage("https://lh3.googleusercontent.com/eOeWjr98uz_E924PnNaXrasepv15nWEuvhqH-jbaQyvrOVdX5MHXF00HdZQGC8NLpJc9ok1sToMtyPx1wnnDgFwTTGA5MjoMFu612AY1")]
		public double StartPercent { get; set; } = 50;

		[DescriptionImage("https://lh3.googleusercontent.com/arAJFTHAOPKn9BQtm1xEyct4LuA2jUAxW11q4cdQPz_JfoCTjS1rxtVTUdE1ND0Q_eigUa27Yc28U08zY2LDiQgS7kKkXKY_FY838p-5")]
		[Description("Split the mesh so it has enough geometry to create a smooth curve")]
		public bool SplitMesh { get; set; } = true;

		[Range(3, 360, ErrorMessage = "Value for {0} must be between {1} and {2}.")]
		[Description("Ensures the rotated part has a minimum number of sides per complete rotation")]
		[DescriptionImage("https://lh3.googleusercontent.com/p9MyKu3AFP55PnobUKZQPqf6iAx11GzXyX-25f1ddrUnfCt8KFGd1YtHOR5HqfO0mhlX2ZVciZV4Yn0Kzfm43SErOS_xzgsESTu9scux")]
		public double MinSidesPerRotation { get; set; } = 30;

		public void DrawEditor(Object3DControlsLayer layer, List<Object3DView> transparentMeshes, DrawEventArgs e)
		{
			var sourceAabb = this.SourceContainer.GetAxisAlignedBoundingBox();
			var distance = Diameter / 2 + sourceAabb.YSize / 2;
			var center = sourceAabb.Center + new Vector3(0, BendDirection == BendDirections.Bend_Up ? distance : -distance, 0);
			center.X -= sourceAabb.XSize / 2 - (StartPercent / 100.0) * sourceAabb.XSize;

			// render the top and bottom rings
			layer.World.RenderCylinderOutline(this.WorldMatrix(), center, Diameter, sourceAabb.ZSize, 100, Color.Red, Color.Transparent);

			// render the split lines
			var radius = Diameter / 2;
			var circumference = MathHelper.Tau * radius;
			var xxx = sourceAabb.XSize * (StartPercent / 100.0);
			var startAngle = MathHelper.Tau * 3 / 4 - xxx / circumference * MathHelper.Tau;
			layer.World.RenderCylinderOutline(this.WorldMatrix(), center, Diameter, sourceAabb.ZSize, (int)Math.Max(0, Math.Min(100, this.MinSidesPerRotation)), Color.Transparent, Color.Red, phase: startAngle);

			// turn the lighting back on
			GL.Enable(EnableCap.Lighting);
		}

		private void DiameterFromAngle()
		{
			var aabb = this.SourceContainer.GetAxisAlignedBoundingBox();
			var angleR = MathHelper.DegreesToRadians(Angle);
			var ratio = angleR / MathHelper.Tau;
			var newDiameter = (aabb.XSize / ratio) / Math.PI;
			if (Math.Abs(Diameter - newDiameter) > .0001)
			{
				Diameter = newDiameter;
				Invalidate(InvalidateType.DisplayValues);
			}
		}


		private void AngleFromDiameter()
		{
			var aabb = this.SourceContainer.GetAxisAlignedBoundingBox();
			var ratio = aabb.XSize / (MathHelper.Tau * Diameter / 2);
			var angleR = MathHelper.Tau * ratio;
			var newAngle = MathHelper.RadiansToDegrees(angleR);
			if (Math.Abs(Angle - newAngle) > .00001)
			{
				Angle = MathHelper.RadiansToDegrees(angleR);
				Invalidate(InvalidateType.DisplayValues);
			}
		}

		public override Task Rebuild()
		{
			this.DebugDepth("Rebuild");

			bool valuesChanged = false;

			// ensure we have good values
			StartPercent = agg_basics.Clamp(StartPercent, 0, 100, ref valuesChanged);

			if (Diameter == double.MaxValue)
			{
				DiameterFromAngle();
			}

			// keep the unused type synced so we don't change the bend when clicking the tabs
			if (BendType == BendTypes.Diameter)
			{
				AngleFromDiameter();
			}
			else
			{
				DiameterFromAngle();
			}

			if (Diameter < 1 || Diameter > 100000)
			{
				Diameter = Math.Min(10000000, Math.Max(.1, Diameter));
				valuesChanged = true;
			}

			MinSidesPerRotation = agg_basics.Clamp(MinSidesPerRotation, 3, 360, ref valuesChanged);

			var rebuildLocks = this.RebuilLockAll();

			return ApplicationController.Instance.Tasks.Execute(
				"Curve".Localize(),
				null,
				(reporter, cancellationToken) =>
				{
					var sourceAabb = this.SourceContainer.GetAxisAlignedBoundingBox();

					var radius = Diameter / 2;
					var circumference = MathHelper.Tau * radius;
					double numRotations = sourceAabb.XSize / circumference;
					double numberOfCuts = numRotations * MinSidesPerRotation;
					double cutSize = sourceAabb.XSize / numberOfCuts;
					double cutPosition = sourceAabb.MinXYZ.X + cutSize;
					var cuts = new List<double>();
					for (int i = 0; i < numberOfCuts; i++)
					{
						cuts.Add(cutPosition);
						cutPosition += cutSize;
					}

					var rotationCenter = new Vector3(sourceAabb.MinXYZ.X + (sourceAabb.MaxXYZ.X - sourceAabb.MinXYZ.X) * (StartPercent / 100),
						BendDirection == BendDirections.Bend_Up ? sourceAabb.MaxXYZ.Y + radius : sourceAabb.MinXYZ.Y - radius,
						sourceAabb.Center.Z);

					var curvedChildren = new List<IObject3D>();

					var status = new ProgressStatus();

					foreach (var sourceItem in SourceContainer.VisibleMeshes())
					{
						var originalMesh = sourceItem.Mesh;
						status.Status = "Copy Mesh".Localize();
						reporter.Report(status);
						var transformedMesh = originalMesh.Copy(CancellationToken.None);
						var itemMatrix = sourceItem.WorldMatrix(SourceContainer);

						// transform into this space
						transformedMesh.Transform(itemMatrix);

						if (SplitMesh)
						{
							status.Status = "Split Mesh".Localize();
							reporter.Report(status);

							// split the mesh along the x axis
							transformedMesh.SplitOnPlanes(Vector3.UnitX, cuts, cutSize / 8);
						}

						for (int i = 0; i < transformedMesh.Vertices.Count; i++)
						{
							var position = transformedMesh.Vertices[i];

							var angleToRotate = ((position.X - rotationCenter.X) / circumference) * MathHelper.Tau - MathHelper.Tau / 4;
							var distanceFromCenter = rotationCenter.Y - position.Y;
							if (BendDirection == BendDirections.Bend_Down)
							{
								angleToRotate = -angleToRotate;
								distanceFromCenter = -distanceFromCenter;
							}

							var rotatePosition = new Vector3Float(Math.Cos(angleToRotate), Math.Sin(angleToRotate), 0) * distanceFromCenter;
							rotatePosition.Z = position.Z;
							transformedMesh.Vertices[i] = rotatePosition + new Vector3Float(rotationCenter.X, radius + sourceAabb.MaxXYZ.Y, 0);
						}

						// transform back into item local space
						transformedMesh.Transform(Matrix4X4.CreateTranslation(-rotationCenter) * itemMatrix.Inverted);

						if (SplitMesh)
						{
							status.Status = "Merge Vertices".Localize();
							reporter.Report(status);

							transformedMesh.MergeVertices(.1);
						}

						transformedMesh.CalculateNormals();

						var curvedChild = new Object3D()
						{
							Mesh = transformedMesh
						};
						curvedChild.CopyWorldProperties(sourceItem, SourceContainer, Object3DPropertyFlags.All, false);
						curvedChild.Visible = true;
						curvedChild.Translate(new Vector3(rotationCenter));
						if (BendDirection == BendDirections.Bend_Down)
						{
							curvedChild.Translate(0, -sourceAabb.YSize - Diameter, 0);
						}

						curvedChildren.Add(curvedChild);
					}

					RemoveAllButSource();
					this.SourceContainer.Visible = false;

					this.Children.Modify((list) =>
					{
						list.AddRange(curvedChildren);
					});

					UiThread.RunOnIdle(() =>
					{
						rebuildLocks.Dispose();
						if (valuesChanged)
						{
							Invalidate(InvalidateType.DisplayValues);
						}
						Parent?.Invalidate(new InvalidateArgs(this, InvalidateType.Children));
					});

					return Task.CompletedTask;
				});
		}

		private Dictionary<string, bool> changeSet = new Dictionary<string, bool>();
	
		public void UpdateControls(PublicPropertyChange change)
		{
			change.SetRowVisible(nameof(Diameter), () => BendType == BendTypes.Diameter);
			change.SetRowVisible(nameof(Angle), () => BendType == BendTypes.Angle);
			change.SetRowVisible(nameof(MinSidesPerRotation), () => SplitMesh);
		}
	}
}