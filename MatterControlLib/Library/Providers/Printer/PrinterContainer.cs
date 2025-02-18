﻿/*
Copyright (c) 2018, John Lewin
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

using System.Collections.Generic;
using System.IO;
using MatterHackers.Agg;
using MatterHackers.Agg.Platform;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.SlicerConfiguration;

namespace MatterHackers.MatterControl.Library
{
	// Printer specific containers
	public class PrinterContainer : LibraryContainer
	{
		private PrinterConfig printer;

		public PrinterContainer(PrinterConfig printer)
		{
			this.printer = printer;
			this.ChildContainers = new SafeList<ILibraryContainerLink>();
			this.Items = new SafeList<ILibraryItem>();
			this.Name = printer.Settings.GetValue(SettingsKey.printer_name);
		}

		public override void Load()
		{
			this.Items.Clear();
			this.ChildContainers.Clear();

			this.ChildContainers.Add(
				new DynamicContainerLink(
					() => "SD Card".Localize(),
					StaticData.Instance.LoadIcon(Path.Combine("Library", "folder.png")),
					StaticData.Instance.LoadIcon(Path.Combine("Library", "sd_icon.png")),
					() => new SDCardContainer(printer),
					() =>
					{
						return printer.Settings.GetValue<bool>(SettingsKey.has_sd_card_reader);
					})
				{
					IsReadOnly = true
				});

			// a new container that holds custom parts for a given printer
			var containerName = $"{printer.Settings.GetValue(SettingsKey.make)} {"Parts".Localize()}";
			var settings = printer.Settings;
			var repository = "Machine_Library_Parts";
			// repository = "PulseOpenSource";
			var subPath = $"{settings.GetValue(SettingsKey.make)}/{settings.GetValue(SettingsKey.model)}";
			// subPath = "C Frame";
			this.ChildContainers.Add(
				new DynamicContainerLink(
					() => containerName,
					StaticData.Instance.LoadIcon(Path.Combine("Library", "folder.png")),
					() => new GitHubContainer(containerName,
						"MatterHackers",
						repository,
						subPath),
					() => printer.Settings.GetValue<bool>(SettingsKey.has_fan)) // visibility (should be base on folder existing)
				{
					IsReadOnly = true
				});

			// TODO: An enumerable list of serialized container paths (or some other markup) to construct for this printer.
			// This would allow for external repositories of parts that are not part of the MH library
			// printer.Settings.GetValue(SettingsKey.library_containers);
		}
	}
}
