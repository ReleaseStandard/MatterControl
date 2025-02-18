﻿/*
Copyright (c) 2017, Lars Brubaker, John Lewin
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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MatterHackers.Agg;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.Localizations;

namespace MatterHackers.MatterControl.Library.Export
{
	public class ZipExport : IExportPlugin
	{
		public string ButtonText => "ZIP File".Localize();

		public string FileExtension => ".zip";

		public string ExtensionFilter => "Save as ZIP|*.zip";

		public int Priority => 3;

		public ImageBuffer Icon { get; } = StaticData.Instance.LoadIcon(Path.Combine("filetypes", "zip.png"));

		public void Initialize(PrinterConfig printer)
		{
		}

		public bool Enabled => true;

		public string DisabledReason => "";

		public bool ExportPossible(ILibraryAsset libraryItem) => true;

		public async Task<List<ValidationError>> Generate(IEnumerable<ILibraryItem> libraryItems, string outputPath, IProgress<ProgressStatus> progress, CancellationToken cancellationToken)
		{
			var streamItems = libraryItems.OfType<ILibraryAssetStream>();
			if (streamItems.Any())
			{
				await Task.Run(async () =>
				{
					try
					{
						if (File.Exists(outputPath))
						{
							File.Delete(outputPath);
						}

						using (ZipArchive zipArchive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
						{
							foreach (var item in streamItems)
							{
								// TODO: need to test for and resolve name conflicts
								var entry = zipArchive.CreateEntry(item.FileName);

								using (var sourceStream = await item.GetStream(null))
								using (var outputStream = entry.Open())
								{
									sourceStream.Stream.CopyTo(outputStream);
								}
							}

						}
					}
					catch (Exception ex)
					{
						Console.WriteLine(ex.Message);
					}
				});

				return null;
			}

			return new List<ValidationError>()
			{
				new ValidationError(ValidationErrors.NoZipItemsToExport)
				{
					Error = "No Items to Export".Localize()
				}
			};
		}
	}
}
