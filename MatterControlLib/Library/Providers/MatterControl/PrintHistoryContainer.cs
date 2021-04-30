﻿/*
Copyright (c) 2017, John Lewin
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
using MatterHackers.Agg;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.PrintHistory;

namespace MatterHackers.MatterControl.Library
{
	public class PrintHistoryContainer : LibraryContainer
	{
		private EventHandler unregisterEvents;

		public PrintHistoryContainer()
		{
			this.ChildContainers = new SafeList<ILibraryContainerLink>();
			this.Items = new SafeList<ILibraryItem>();
			this.Name = "Print History".Localize();
			this.DefaultView = typeof(HistoryListView);

			PrintHistoryData.Instance.HistoryCleared.RegisterEvent(HistoryChanged, ref unregisterEvents);
			ApplicationController.Instance.AnyPrintStarted += HistoryChanged;
			ApplicationController.Instance.AnyPrintCanceled += HistoryChanged;
			ApplicationController.Instance.AnyPrintComplete += HistoryChanged;
		}

		private void HistoryChanged(object sender, EventArgs e)
		{
			ReloadContent();
		}

		public override void Dispose()
		{
			unregisterEvents?.Invoke(this, null);

			base.Dispose();
		}

		public override void Load()
		{
			// PrintItems projected onto FileSystemFileItem
			Items = new SafeList<ILibraryItem>(PrintHistoryData.Instance.GetHistoryItems(50).Select(f => new PrintHistoryItem(f)));
		}
	}
}
