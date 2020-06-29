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

using global::MatterControl.Printing;
using MatterHackers.Agg;
using MatterHackers.Agg.Font;
using MatterHackers.Agg.Image;
using MatterHackers.Agg.Platform;
using MatterHackers.Agg.UI;
using MatterHackers.Agg.VertexSource;
using MatterHackers.DataConverters3D;
using MatterHackers.DataConverters3D.UndoCommands;
using MatterHackers.Localizations;
using MatterHackers.MatterControl.CustomWidgets;
using MatterHackers.MatterControl.DataStorage;
using MatterHackers.MatterControl.DesignTools;
using MatterHackers.MatterControl.DesignTools.Operations;
using MatterHackers.MatterControl.Extensibility;
using MatterHackers.MatterControl.Library;
using MatterHackers.MatterControl.PartPreviewWindow;
using MatterHackers.MatterControl.PartPreviewWindow.View3D;
using MatterHackers.MatterControl.Plugins;
using MatterHackers.MatterControl.PrinterCommunication;
using MatterHackers.MatterControl.PrinterControls.PrinterConnections;
using MatterHackers.MatterControl.PrintQueue;
using MatterHackers.MatterControl.SettingsManagement;
using MatterHackers.MatterControl.SlicerConfiguration;
using MatterHackers.MatterControl.Tour;
using MatterHackers.PolygonMesh;
using MatterHackers.PolygonMesh.Processors;
using MatterHackers.VectorMath;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("MatterControl.Tests")]
[assembly: InternalsVisibleTo("MatterControl.AutomationTests")]
[assembly: InternalsVisibleTo("CloudServices.Tests")]

namespace MatterHackers.MatterControl
{
	[JsonConverter(typeof(StringEnumConverter))]
	public enum NamedTypeFace
	{
		Alfa_Slab,
		Audiowide,
		Bangers,
		Courgette,
		Damion,
		Firefly_Sung,
		Fredoka,
		Great_Vibes,
		Liberation_Mono,
		Liberation_Sans,
		Liberation_Sans_Bold,
		Lobster,
		Nunito_Regular,
		Nunito_Bold,
		Nunito_Bold_Italic,
		Nunito_Italic,
		Pacifico,
		Poppins,
		Questrial,
		Righteous,
		Russo,
		Titan,
		Titillium,
	}

	public class ApplicationController
	{
		public event EventHandler<string> ApplicationError;

		public event EventHandler<string> ApplicationEvent;

		public HelpArticle HelpArticles { get; set; }

		public ThemeConfig Theme => AppContext.Theme;

		public ThemeConfig MenuTheme => AppContext.MenuTheme;

		public event EventHandler<string> ShellFileOpened;

		public bool IsMatterControlPro()
		{
			var result = ApplicationController.Instance.UserHasPermissionToId?.Invoke("ag1zfm1oLWRmcy1wcm9kchgLEgtEaWdpdGFsSXRlbRiAgIDzyMGxCgw");
			if (result != null)
			{
				return result.Value;
			}

			return false;
		}

		public RunningTasksConfig Tasks { get; set; } = new RunningTasksConfig();

		public IEnumerable<PrinterConfig> ActivePrinters => this.Workspaces.Where(w => w.Printer != null).Select(w => w.Printer);

		public ExtensionsConfig Extensions { get; }

		public PopupMenu GetActionMenuForSceneItem(IObject3D selectedItem, InteractiveScene scene, bool addInSubmenu, View3DWidget view3DWidget, IEnumerable<NodeOperation> nodeOperations = null)
		{
			// If parameter was not supplied, fall back to unfiltered list of operations
			if (nodeOperations == null)
			{
				nodeOperations = this.Graph.Operations.Values;
			}

			var popupMenu = new PopupMenu(this.MenuTheme);
			var selectedItemType = selectedItem.GetType();
			var menuTheme = this.MenuTheme;

			if (!selectedItemType.IsDefined(typeof(ImmutableAttribute), false))
			{
				AddModifyMenuItems(selectedItem, scene, addInSubmenu, nodeOperations, menuTheme, popupMenu, selectedItemType);
			}

			var workspaceActions = GetWorkspaceActions(view3DWidget);
			var sceneContext = view3DWidget.sceneContext;
			var printer = view3DWidget.Printer;

			var actions = new[]
			{
				new ActionSeparator(),
				workspaceActions["Edit"],
				new ActionSeparator(),
				new NamedAction()
				{
			 		Title = "Save As".Localize(),
			 		Action = () => UiThread.RunOnIdle(() =>
					{
						DialogWindow.Show(
							new SaveAsPage(
								async (newName, destinationContainer) =>
								{
									// Save to the destination provider
									if (destinationContainer is ILibraryWritableContainer writableContainer)
									{
										// Wrap stream with ReadOnlyStream library item and add to container
										writableContainer.Add(new[]
										{
											new InMemoryLibraryItem(selectedItem)
											{
												Name = newName
											}
										});

										destinationContainer.Dispose();
									}
								}));
					}),
			 		IsEnabled = () => sceneContext.EditableScene
				},
				new NamedAction()
				{
					ID = "Export",
					Title = "Export".Localize(),
					Icon = AggContext.StaticData.LoadIcon("cube_export.png", 16, 16, menuTheme.InvertIcons),
					Action = () =>
					{
						ApplicationController.Instance.ExportLibraryItems(
							new[] { new InMemoryLibraryItem(selectedItem) },
							centerOnBed: false,
							printer: printer);
					}
				},
				new ActionSeparator(),
				workspaceActions["Delete"]
			};

			menuTheme.CreateMenuItems(popupMenu, actions);

			if (selectedItem is ComponentObject3D componentObject)
			{
				popupMenu.CreateSeparator();

				string componentID = componentObject.ComponentID;

				var helpItem = popupMenu.CreateMenuItem("Help".Localize());
				helpItem.Enabled = !string.IsNullOrEmpty(componentID) && this.HelpArticlesByID.ContainsKey(componentID);
				helpItem.Click += (s, e) =>
				{
					var helpTab = ApplicationController.Instance.ActivateHelpTab();
					if (helpTab.TabContent is HelpTreePanel helpTreePanel)
					{
						if (this.HelpArticlesByID.TryGetValue(componentID, out HelpArticle helpArticle))
						{
							helpTreePanel.ActiveNodePath = componentID;
						}
					}
				};
			}

			return popupMenu;
		}

		public PopupMenu GetModifyMenu(IObject3D selectedItem, InteractiveScene scene, IEnumerable<NodeOperation> nodeOperations = null)
		{
			var popupMenu = new PopupMenu(this.MenuTheme);

			AddModifyMenuItems(
				selectedItem,
				scene,
				false,
				nodeOperations,
				this.MenuTheme,
				popupMenu,
				selectedItem.GetType());

			return popupMenu;
		}

		private static void AddModifyMenuItems(IObject3D selectedItem, InteractiveScene scene, bool useSubMenu, IEnumerable<NodeOperation> nodeOperations, ThemeConfig menuTheme, PopupMenu popupMenu, Type selectedItemType)
		{
			void AddItems(PopupMenu menu)
			{
				foreach (var nodeOperation in nodeOperations)
				{
					foreach (var type in nodeOperation.MappedTypes)
					{
						if (type.IsAssignableFrom(selectedItemType)
							&& (nodeOperation.IsVisible?.Invoke(selectedItem) != false)
							&& nodeOperation.IsEnabled?.Invoke(selectedItem) != false)
						{
							var menuItem = menu.CreateMenuItem(nodeOperation.Title, nodeOperation.IconCollector?.Invoke(menuTheme.InvertIcons));
							menuItem.Click += (s, e) =>
							{
								nodeOperation.Operation(selectedItem, scene).ConfigureAwait(false);
							};
						}
					}
				}
			}

			var renameMenuItem = popupMenu.CreateMenuItem("Rename".Localize());
			renameMenuItem.Click += (s, e) =>
			{
				DialogWindow.Show(
					new InputBoxPage(
						"Rename Item".Localize(),
						"Name".Localize(),
						selectedItem.Name,
						"Enter New Name Here".Localize(),
						"Rename".Localize(),
						(newName) =>
						{
							// TODO: add undo data to this operation
							selectedItem.Name = newName;
						}));
			};

			popupMenu.CreateSeparator();

			if (useSubMenu)
			{
				// Create items in a 'Modify' sub-menu
				popupMenu.CreateSubMenu("Modify".Localize(), menuTheme, (modifyMenu) => AddItems(modifyMenu));
			}
			else
			{
				// Create items directly in the referenced menu
				AddItems(popupMenu);
			}
		}

		public async Task PersistUserTabs()
		{
			// Persist all pending changes in all workspaces to disk
			foreach (var workspace in this.Workspaces.ToArray())
			{
				await this.Tasks.Execute("Saving ".Localize() + $" \"{workspace.Name}\" ...", workspace, workspace.SceneContext.SaveChanges);
			}

			// Project workspace definitions to serializable structure
			var workspaces = this.Workspaces.Select(w =>
			{
				if (w.Printer == null)
				{
					return new PartWorkspace(w.SceneContext)
					{
						ContentPath = w.SceneContext.EditContext?.SourceFilePath,
					};
				}
				else
				{
					return new PartWorkspace(w.Printer)
					{
						ContentPath = w.SceneContext.EditContext?.SourceFilePath,
					};
				}
			});

			// Persist workspace definitions to disk
			File.WriteAllText(
				ProfileManager.Instance.OpenTabsPath,
				JsonConvert.SerializeObject(
					workspaces,
					Formatting.Indented,
					new JsonSerializerSettings
					{
						NullValueHandling = NullValueHandling.Ignore
					}));
		}

		internal void ExportAsMatterControlConfig(PrinterConfig printer)
		{
			AggContext.FileDialogs.SaveFileDialog(
				new SaveFileDialogParams("MatterControl Printer Export|*.printer", title: "Export Printer Settings")
				{
					FileName = printer.Settings.GetValue(SettingsKey.printer_name)
				},
				(saveParams) =>
				{
					try
					{
						if (!string.IsNullOrWhiteSpace(saveParams.FileName))
						{
							File.WriteAllText(saveParams.FileName, JsonConvert.SerializeObject(printer.Settings, Formatting.Indented));
						}
					}
					catch (Exception e)
					{
						UiThread.RunOnIdle(() =>
						{
							StyledMessageBox.ShowMessageBox(e.Message, "Couldn't save file".Localize());
						});
					}
				});
		}

		public void LogError(string errorMessage)
		{
			this.ApplicationError?.Invoke(this, errorMessage);
		}

		public void LogInfo(string message)
		{
			this.ApplicationEvent?.Invoke(this, message);
		}

		public Action RedeemDesignCode { get; set; }

		public Action EnterShareCode { get; set; }

		// check permission to an IObject3D instance
		public Func<IObject3D, bool> UserHasPermission { get; set; } = (item) => false;

		// check permission to a purchase
		public Func<string, bool> UserHasPermissionToId { get; set; }

		public Func<IObject3D, ThemeConfig, (string url, GuiWidget markdownWidget)> GetUnlockData { get; set; }

		private static ApplicationController globalInstance;

		public RootedObjectEventHandler CloudSyncStatusChanged { get; private set; } = new RootedObjectEventHandler();
		public RootedObjectEventHandler DoneReloadingAll = new RootedObjectEventHandler();
		public RootedObjectEventHandler ActiveProfileModified = new RootedObjectEventHandler();

		public event EventHandler<WorkspacesChangedEventArgs> WorkspacesChanged;

		public static Action WebRequestFailed;
		public static Action WebRequestSucceeded;

		public static Action<DialogWindow> ChangeToPrintNotification = null;

#if DEBUG
		public const string EnvironmentName = "TestEnv_";
#else
		public const string EnvironmentName = "";
#endif

		public bool ApplicationExiting { get; internal set; } = false;

		public static Func<string, Task<Dictionary<string, string>>> GetProfileHistory;

		public void OnWorkspacesChanged(WorkspacesChangedEventArgs e)
		{
			this.WorkspacesChanged?.Invoke(this, e);

			if (e.Operation != WorkspacesChangedEventArgs.OperationType.Restore)
			{
				UiThread.RunOnIdle(async () =>
				{
					await ApplicationController.Instance.PersistUserTabs();
				});
			}
		}

		public string GetFavIconUrl(string oemName)
		{
			if (OemSettings.Instance.OemUrls.TryGetValue(oemName, out string oemUrl)
				&& !string.IsNullOrWhiteSpace(oemUrl))
			{
				return "https://www.google.com/s2/favicons?domain=" + oemUrl;
			}

			return null;
		}

		public void ClosePrinter(PrinterConfig printer, bool allowChangedEvent = true)
		{
			// Actually clear printer
			ProfileManager.Instance.ClosePrinter(printer.Settings.ID);

			// Shutdown the printer connection
			printer.Connection.Disable();

			if (allowChangedEvent)
			{
				if (this.Workspaces.FirstOrDefault(w => w.Printer?.Settings.ID == printer.Settings.ID) is PartWorkspace workspace)
				{
					this.Workspaces.Remove(workspace);

					this.OnWorkspacesChanged(
						new WorkspacesChangedEventArgs(
							workspace,
							WorkspacesChangedEventArgs.OperationType.Remove));
				}
			}

			printer.Dispose();
		}

		public void LaunchBrowser(string targetUri)
		{
			UiThread.RunOnIdle(() =>
			{
				if (!string.IsNullOrEmpty(OemSettings.Instance.AffiliateCode)
					&& targetUri.Contains("matterhackers.com"))
				{
					string internalLink = "";
					// if we have a trailing internal link
					if (targetUri.Contains("#"))
					{
						internalLink = targetUri.Substring(targetUri.IndexOf("#"));
						targetUri = targetUri.Substring(0, targetUri.Length - internalLink.Length);
					}

					if (targetUri.Contains("?"))
					{
						targetUri += $"&aff={OemSettings.Instance.AffiliateCode}";
					}
					else
					{
						targetUri += $"?aff={OemSettings.Instance.AffiliateCode}";
					}

					targetUri += internalLink;
				}

				Process.Start(targetUri);
			});
		}

		internal void MakeGrayscale(ImageBuffer sourceImage)
		{
			var buffer = sourceImage.GetBuffer();
			int destIndex = 0;
			for (int y = 0; y < sourceImage.Height; y++)
			{
				for (int x = 0; x < sourceImage.Width; x++)
				{
					int b = buffer[destIndex + 0];
					int g = buffer[destIndex + 1];
					int r = buffer[destIndex + 2];

					int c = (r * 77) + (g * 151) + (b * 28);
					byte gray = (byte)(c >> 8);

					buffer[destIndex + 0] = gray;
					buffer[destIndex + 1] = gray;
					buffer[destIndex + 2] = gray;

					destIndex += 4;
				}
			}
		}

		// Plugin Registration Points

		// Returns the user printer profile from the webservices plugin
		public static Func<PrinterInfo, string, Task<PrinterSettings>> GetPrinterProfileAsync;

		// Executes the user printer profile sync logic in the webservices plugin
		public static Func<string, IProgress<ProgressStatus>, Task> SyncCloudProfiles;

		public static Action<string> QueueCloudProfileSync;

		// Returns all public printer profiles from the webservices plugin
		public static Func<Task<OemProfileDictionary>> GetPublicProfileList;

		// Returns the public printer profile from the webservices plugin
		public static Func<string, Task<PrinterSettings>> DownloadPublicProfileAsync;

		// Indicates if guest, rather than an authenticated user, is active
		public static Func<bool> GuestUserActive { get; set; }

		// Returns the authentication dialog from the authentication plugin
		public static Func<AuthenticationContext, DialogPage> GetAuthPage;

		public SlicePresetsPage EditMaterialPresetsPage { get; set; }

		public SlicePresetsPage EditQualityPresetsWindow { get; set; }

		public MainViewWidget MainView;

		private readonly Dictionary<string, List<LibraryAction>> registeredLibraryActions = new Dictionary<string, List<LibraryAction>>();

		private List<SceneSelectionOperation> registeredSceneOperations;

		public ThumbnailsConfig Thumbnails { get; }

		private void BuildSceneOperations()
		{
			OperationSourceContainerObject3D.TaskBuilder = (name, func) => ApplicationController.Instance.Tasks.Execute(name, null, func);

			registeredSceneOperations = new List<SceneSelectionOperation>()
			{
				new SceneSelectionOperation()
				{
					OperationType = typeof(GroupObject3D),

					TitleResolver = () => "Group".Localize(),
					Action = (sceneContext) =>
					{
						var scene = sceneContext.Scene;
						var selectedItem = scene.SelectedItem;
						scene.SelectedItem = null;

						var newGroup = new GroupObject3D();
						// When grouping items, move them to be centered on their bounding box
						newGroup.Children.Modify((gChildren) =>
						{
							selectedItem.Clone().Children.Modify((sChildren) =>
							{
								var center = selectedItem.GetAxisAlignedBoundingBox().Center;

								foreach (var child in sChildren)
								{
									child.Translate(-center.X, -center.Y, 0);
									gChildren.Add(child);
								}

								newGroup.Translate(center.X, center.Y, 0);
							});
						});

						scene.UndoBuffer.AddAndDo(new ReplaceCommand(selectedItem.Children.ToList(), new[] { newGroup }));

						newGroup.MakeNameNonColliding();

						scene.SelectedItem = newGroup;
					},
					HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
					IsEnabled = (sceneContext) => sceneContext.Scene is InteractiveScene scene
						&& scene.SelectedItem != null
						&& scene.SelectedItem is SelectionGroupObject3D
						&& scene.SelectedItem.Children.Count > 1,
					Icon = (invertIcon) => AggContext.StaticData.LoadIcon("group.png", 16, 16).SetPreMultiply(),
				},
				new SceneSelectionOperation()
				{
					TitleResolver = () => "Ungroup".Localize(),
					Action = (sceneContext) => sceneContext.Scene.UngroupSelection(),
					HelpTextResolver = () => "*A single part must be selected*".Localize(),
					IsEnabled = (sceneContext) =>
					{
						var selectedItem = sceneContext.Scene.SelectedItem;
						if (selectedItem != null)
						{
							return selectedItem is GroupObject3D
								|| selectedItem.GetType() == typeof(Object3D)
								|| selectedItem.CanFlatten;
						}

						return false;
					},
					Icon = (invertIcon) => AggContext.StaticData.LoadIcon("ungroup.png", 16, 16).SetPreMultiply(),
				},
				new SceneSelectionSeparator(),
				new SceneSelectionOperation()
				{
					TitleResolver = () => "Duplicate".Localize(),
					Action = (sceneContext) => sceneContext.DuplicateItem(5),
					HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
					IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
					Icon = (invertIcon) => AggContext.StaticData.LoadIcon("duplicate.png").SetPreMultiply(),
				},
				new SceneSelectionOperation()
				{
					TitleResolver = () => "Remove".Localize(),
					Action = (sceneContext) => sceneContext.Scene.DeleteSelection(),
					HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
					IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
					Icon = (invertIcon) => AggContext.StaticData.LoadIcon("remove.png").SetPreMultiply(),
				},
				new SceneSelectionSeparator(),
				new OperationGroup("Align")
				{
					TitleResolver = () => "Align".Localize(),
					StickySelection = true,
					Operations = new List<SceneSelectionOperation>()
					{
						new SceneSelectionOperation()
						{
							TitleResolver = () => "Arrange All Parts".Localize(),
							Action = async (sceneContext) =>
							{
								await sceneContext.Scene.AutoArrangeChildren(new Vector3(sceneContext.BedCenter)).ConfigureAwait(false);
							},
							HelpTextResolver = () => "*No part to arrange*".Localize(),
							IsEnabled = (sceneContext) =>
							{
								return sceneContext.EditableScene && sceneContext.Scene.VisibleMeshes().Any();
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("arrange_all.png", 16, 16, invertIcon).SetPreMultiply(),
						},
						new SceneSelectionOperation()
						{
							TitleResolver = () => "Lay Flat".Localize(),
							Action = (sceneContext) =>
							{
								var scene = sceneContext.Scene;
								var selectedItem = scene.SelectedItem;
								if (selectedItem != null)
								{
									try
									{
										scene.MakeLowestFaceFlat(selectedItem);
									}
									catch
									{
									}
								}
							},
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("lay_flat.png", 16, 16, invertIcon).SetPreMultiply(),
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(AlignObject3D),
							TitleResolver = () => "Align".Localize(),
							Action = (sceneContext) =>
							{
								var scene = sceneContext.Scene;
								var selectedItem = scene.SelectedItem;
								var align = new AlignObject3D();
								align.AddSelectionAsChildren(scene, selectedItem);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("align_left_dark.png", 16, 16, invertIcon).SetPreMultiply(),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem is SelectionGroupObject3D,
						},
					},
				},
				new OperationGroup("Booleans")
				{
					TitleResolver = () => "Booleans".Localize(),
					StickySelection = true,
					Operations = new List<SceneSelectionOperation>()
					{
						new SceneSelectionOperation()
						{
							OperationType = typeof(CombineObject3D_2),
							TitleResolver = () => "Combine".Localize(),
							Action = (sceneContext) => new CombineObject3D_2().WrapSelectedItemAndSelect(sceneContext.Scene),
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("combine.png").SetPreMultiply(),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) =>
							{
								var selectedItem = sceneContext.Scene.SelectedItem;
								return selectedItem != null && selectedItem.VisibleMeshes().Count() > 1;
							},
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(SubtractObject3D_2),
							TitleResolver = () => "Subtract".Localize(),
							Action = (sceneContext) => new SubtractObject3D_2().WrapSelectedItemAndSelect(sceneContext.Scene),
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("subtract.png").SetPreMultiply(),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) =>
							{
								var selectedItem = sceneContext.Scene.SelectedItem;
								return selectedItem != null && selectedItem.VisibleMeshes().Count() > 1;
							},
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(IntersectionObject3D_2),
							TitleResolver = () => "Intersect".Localize(),
							Action = (sceneContext) => new IntersectionObject3D_2().WrapSelectedItemAndSelect(sceneContext.Scene),
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("intersect.png"),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) =>
							{
								var selectedItem = sceneContext.Scene.SelectedItem;
								return selectedItem != null && selectedItem.VisibleMeshes().Count() > 1;
							},
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(SubtractAndReplaceObject3D_2),
							TitleResolver = () => "Subtract & Replace".Localize(),
							Action = (sceneContext) => new SubtractAndReplaceObject3D_2().WrapSelectedItemAndSelect(sceneContext.Scene),
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("subtract_and_replace.png").SetPreMultiply(),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) =>
							{
								var selectedItem = sceneContext.Scene.SelectedItem;
								return selectedItem != null && selectedItem.VisibleMeshes().Count() > 1;
							},
						}
					}
				},
				new OperationGroup("Array")
				{
					Collapse = true,
					TitleResolver = () => "Array Options".Localize(),
					StickySelection = true,
					Operations = new List<SceneSelectionOperation>()
					{
						new SceneSelectionOperation()
						{
							OperationType = typeof(ArrayLinearObject3D),
							TitleResolver = () => "Linear Array".Localize(),
							Action = (sceneContext) =>
							{
								var array = new ArrayLinearObject3D();
								array.AddSelectionAsChildren(sceneContext.Scene, sceneContext.Scene.SelectedItem);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("array_linear.png").SetPreMultiply(),
							HelpTextResolver = () => "*A single part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null && !(sceneContext.Scene.SelectedItem is SelectionGroupObject3D),
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(ArrayRadialObject3D),
							TitleResolver = () => "Radial Array".Localize(),
							Action = (sceneContext) =>
							{
								var array = new ArrayRadialObject3D();
								array.AddSelectionAsChildren(sceneContext.Scene, sceneContext.Scene.SelectedItem);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("array_radial.png").SetPreMultiply(),
							HelpTextResolver = () => "*A single part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null && !(sceneContext.Scene.SelectedItem is SelectionGroupObject3D),
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(ArrayAdvancedObject3D),
							TitleResolver = () => "Advanced Array".Localize(),
							Action = (sceneContext) =>
							{
								var array = new ArrayAdvancedObject3D();
								array.AddSelectionAsChildren(sceneContext.Scene, sceneContext.Scene.SelectedItem);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("array_advanced.png").SetPreMultiply(),
							HelpTextResolver = () => "*A single part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null && !(sceneContext.Scene.SelectedItem is SelectionGroupObject3D),
						}
					}
				},
				new OperationGroup("ModifyMesh")
				{
					Collapse = true,
					TitleResolver = () => "Mesh Modifiers".Localize(),
					StickySelection = true,
					Operations = new List<SceneSelectionOperation>()
					{
						new SceneSelectionOperation()
						{
							OperationType = typeof(CurveObject3D_2),
							TitleResolver = () => "Curve".Localize(),
							Action = (sceneContext) =>
							{
								var curve = new CurveObject3D_2();
								curve.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("curve.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(PinchObject3D_2),
							TitleResolver = () => "Pinch".Localize(),
							Action = (sceneContext) =>
							{
								var pinch = new PinchObject3D_2();
								pinch.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("pinch.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(TwistObject3D),
							TitleResolver = () => "Twist".Localize(),
							Action = (sceneContext) =>
							{
								var curve = new TwistObject3D();
								curve.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("twist.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
#if DEBUG // don't make this part of the distribution until it is working
						new SceneSelectionOperation()
						{
							OperationType = typeof(PlaneCutObject3D),
							TitleResolver = () => "Plane Cut".Localize(),
							Action = (sceneContext) =>
							{
								var cut = new PlaneCutObject3D();
								cut.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("plane_cut.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
#endif
						new SceneSelectionOperation()
						{
							OperationType = typeof(HollowOutObject3D),
							TitleResolver = () => "Hollow Out".Localize(),
							Action = (sceneContext) =>
							{
								var hollowOut = new HollowOutObject3D();
								hollowOut.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("hollow.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(DecimateObject3D),
							TitleResolver = () => "Reduce".Localize(),
							Action = (sceneContext) =>
							{
								var hollowOut = new DecimateObject3D();
								hollowOut.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("reduce.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(RepairObject3D),
							TitleResolver = () => "Repair".Localize(),
							Action = (sceneContext) =>
							{
								var hollowOut = new RepairObject3D();
								hollowOut.WrapSelectedItemAndSelect(sceneContext.Scene);
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("repair.png", 16, 16, invertIcon),
							HelpTextResolver = () => "*At least 1 part must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null,
						},
					}
				},
				new OperationGroup("Other")
				{
					TitleResolver = () => "Other".Localize(),
					StickySelection = true,
					Operations = new List<SceneSelectionOperation>()
					{
						new SceneSelectionOperation()
						{
							OperationType = typeof(AlignObject3D),
							TitleResolver = () => "Dual Extrusion Align".Localize(),
							Action = (sceneContext) =>
							{
								var scene = sceneContext.Scene;
								var selectedItem = scene.SelectedItem;

								if (selectedItem is SelectionGroupObject3D selectionGroup)
								{
									var first = selectionGroup.Children.FirstOrDefault();
									var center = first.GetCenter();
									var startMatrix = first.Matrix;
									first.Matrix = Matrix4X4.Identity;
									var offset = center - first.GetCenter();
									first.Matrix = startMatrix;

									var transformData = selectionGroup.Children.Select(c => new TransformData()
									{
										TransformedObject = c,
										UndoTransform = c.Matrix,
										RedoTransform = Matrix4X4.CreateTranslation(offset)
									}).ToList();

									scene.UndoBuffer.AddAndDo(new TransformCommand(transformData));
								}
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("dual_align.png", 16, 16, invertIcon).SetPreMultiply(),
							HelpTextResolver = () => "*At least 2 parts must be selected*".Localize(),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem is SelectionGroupObject3D,
						},
						new SceneSelectionOperation()
						{
							OperationType = typeof(FitToBoundsObject3D_2),
							TitleResolver = () => "Fit to Bounds".Localize(),
							Action = async (sceneContext) =>
							{
								var scene = sceneContext.Scene;
								var selectedItem = scene.SelectedItem;
								using (new SelectionMaintainer(scene))
								{
									var fit = await FitToBoundsObject3D_2.Create(selectedItem.Clone());
									fit.MakeNameNonColliding();

									scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { selectedItem }, new[] { fit }));
								}
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("fit.png", 16, 16, invertIcon),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null && !(sceneContext.Scene.SelectedItem is SelectionGroupObject3D),
						},

		#if DEBUG
						new SceneSelectionOperation()
						{
							OperationType = typeof(FitToCylinderObject3D),
							TitleResolver = () => "Fit to Cylinder".Localize(),
							Action = async (sceneContext) =>
							{
								var scene = sceneContext.Scene;
								var selectedItem = scene.SelectedItem;
								using (new SelectionMaintainer(scene))
								{
									var fit = await FitToCylinderObject3D.Create(selectedItem.Clone());
									fit.MakeNameNonColliding();

									scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { selectedItem }, new[] { fit }));
								}
							},
							Icon = (invertIcon) => AggContext.StaticData.LoadIcon("fit.png", 16, 16, invertIcon),
							IsEnabled = (sceneContext) => sceneContext.Scene.SelectedItem != null && !(sceneContext.Scene.SelectedItem is SelectionGroupObject3D),
						},
#endif
					},
				},
			};

			var operationIconsByType = new Dictionary<Type, Func<bool, ImageBuffer>>();

			foreach (var operation in registeredSceneOperations)
			{
				if (operation.OperationType != null)
				{
					operationIconsByType.Add(operation.OperationType, operation.Icon);
				}
			}

			// TODO: Use custom selection group icon if reusing group icon seems incorrect
			//
			// Explicitly register SelectionGroup icon
			if (operationIconsByType.TryGetValue(typeof(GroupObject3D), out Func<bool, ImageBuffer> groupIconSource))
			{
				operationIconsByType.Add(typeof(SelectionGroupObject3D), groupIconSource);
			}

			this.Thumbnails.OperationIcons = operationIconsByType;

			operationIconsByType.Add(typeof(ImageObject3D), (invertIcon) => AggContext.StaticData.LoadIcon("140.png", 16, 16, invertIcon));
		}

		public Dictionary<string, NamedAction> GetWorkspaceActions(View3DWidget view3DWidget)
		{
			var sceneContext = view3DWidget.sceneContext;
			var printer = sceneContext.Printer;

			bool invertIcons = ApplicationController.Instance.MenuTheme.InvertIcons;

			// Build workspace actions, each having a unique ID
			var actions = new[]
			{
				new NamedAction()
				{
					ID = "Print",
					Title = "Print".Localize(),
					Shortcut = "Ctrl+P",
					Action = view3DWidget.PushToPrinterAndPrint,
					IsEnabled = () => sceneContext.EditableScene
						|| (sceneContext.EditContext.SourceItem is ILibraryAsset libraryAsset
							&& string.Equals(Path.GetExtension(libraryAsset.FileName), ".gcode", StringComparison.OrdinalIgnoreCase))
				},
				new NamedActionGroup()
				{
					ID = "Edit",
					Title = "Edit",
					Group = new NamedAction[]
					{
						new NamedAction()
						{
							ID = "Cut",
							Title = "Cut".Localize(),
							Action = () => sceneContext.Scene.Cut(),
							IsEnabled = () => sceneContext.Scene.SelectedItem != null
						},
						new NamedAction()
						{
							ID = "Copy",
							Title = "Copy".Localize(),
							Action = () => sceneContext.Scene.Copy(),
							IsEnabled = () => sceneContext.Scene.SelectedItem != null
						},
						new NamedAction()
						{
							ID = "Paste",
							Title = "Paste".Localize(),
							Action = () => sceneContext.Paste(),
							IsEnabled = () => Clipboard.Instance.ContainsImage || Clipboard.Instance.GetText() == "!--IObjectSelection--!"
						}
					},
					IsEnabled = () => true,
				},
				new NamedAction()
				{
					ID = "Delete",
					Icon = AggContext.StaticData.LoadIcon("remove.png").SetPreMultiply(),
					Title = "Remove".Localize(),
					Action = sceneContext.Scene.DeleteSelection,
					IsEnabled = () => sceneContext.Scene.SelectedItem != null
				},
				new NamedAction()
				{
					ID = "Export",
					Title = "Export".Localize(),
					Icon = AggContext.StaticData.LoadIcon("cube_export.png", 16, 16, invertIcons),
					Action = () =>
					{
						ApplicationController.Instance.ExportLibraryItems(
							new[] { new InMemoryLibraryItem(sceneContext.Scene) },
							centerOnBed: false,
							printer: printer);
					},
					IsEnabled = () => sceneContext.EditableScene
						|| (sceneContext.EditContext.SourceItem is ILibraryAsset libraryAsset
							&& string.Equals(Path.GetExtension(libraryAsset.FileName), ".gcode", StringComparison.OrdinalIgnoreCase))
				},
				new NamedAction()
				{
					ID = "Save",
					Title = "Save".Localize(),
					Shortcut = "Ctrl+S",
					Action = () =>
					{
						ApplicationController.Instance.Tasks.Execute("Saving".Localize(), printer, sceneContext.SaveChanges).ConfigureAwait(false);
					},
					IsEnabled = () => sceneContext.EditableScene
				},
				new NamedAction()
				{
					ID = "SaveAs",
					Title = "Save As".Localize(),
					Action = () => UiThread.RunOnIdle(() =>
					{
						DialogWindow.Show(
							new SaveAsPage(
								async (newName, destinationContainer) =>
								{
									// Save to the destination provider
									if (destinationContainer is ILibraryWritableContainer writableContainer)
									{
										// Wrap stream with ReadOnlyStream library item and add to container
										writableContainer.Add(new[]
										{
											new InMemoryLibraryItem(sceneContext.Scene)
											{
												Name = newName
											}
										});

										destinationContainer.Dispose();
									}
								}));
					}),
					IsEnabled = () => sceneContext.EditableScene
				},
				new NamedAction()
				{
					ID = "ArrangeAll",
					Title = "Arrange All Parts".Localize(),
					Action = async () =>
					{
						await sceneContext.Scene.AutoArrangeChildren(view3DWidget.BedCenter).ConfigureAwait(false);
					},
					IsEnabled = () => sceneContext.EditableScene
				},
				new NamedAction()
				{
					ID = "ClearBed",
					Title = "Clear Bed".Localize(),
					Action = () =>
					{
						UiThread.RunOnIdle(() =>
						{
							view3DWidget.ClearPlate();
						});
					}
				}
			};

			// Construct dictionary from workspace actions by ID
			return actions.ToDictionary(a => a.ID);
		}

		public void OpenIntoNewTab(IEnumerable<ILibraryItem> selectedLibraryItems)
		{
			this.MainView.CreatePartTab().ContinueWith(task =>
			{
				var workspace = this.Workspaces.Last();
				workspace.SceneContext.AddToPlate(selectedLibraryItems);
			});
		}

		internal void BlinkTab(ITab tab)
		{
			var theme = this.Theme;
			if (tab is GuiWidget guiWidget)
			{
				guiWidget.Descendants<TextWidget>().FirstOrDefault().FlashBackground(theme.PrimaryAccentColor.WithContrast(theme.TextColor, 6).ToColor());
			}
		}

		public void ShowApplicationHelp()
		{
			this.ActivateHelpTab();
		}

		public void ShowInterfaceTour()
		{
			UiThread.RunOnIdle(ProductTour.StartTour);
		}

		public void ShowAboutPage()
		{
			UiThread.RunOnIdle(() =>
			{
				DialogWindow.Show<AboutPage>();
			});
		}

		public ImageSequence GetProcessingSequence(Color color)
		{
			int size = (int)Math.Round(80 * GuiWidget.DeviceScale);
			double radius = size / 8.0;
			var workingAnimation = new ImageSequence();
			var frameCount = 30.0;
			var strokeWidth = 4 * GuiWidget.DeviceScale;

			for (int i = 0; i < frameCount; i++)
			{
				var frame = new ImageBuffer(size, size);
				var graphics = frame.NewGraphics2D();
				graphics.Render(new Stroke(new Arc(frame.Width / 2,
					frame.Height / 2,
					size / 4 - strokeWidth / 2,
					size / 4 - strokeWidth / 2,
					MathHelper.Tau / frameCount * i,
					MathHelper.Tau / 4 + MathHelper.Tau / frameCount * i),
					strokeWidth),
					color);
				workingAnimation.AddImage(frame);
			}

			return workingAnimation;
		}

		private static int applicationInstanceCount = 0;

		public static int ApplicationInstanceCount
		{
			get
			{
				if (applicationInstanceCount == 0)
				{
					Assembly mcAssembly = Assembly.GetEntryAssembly();
					if (mcAssembly != null)
					{
						string applicationName = Path.GetFileNameWithoutExtension(mcAssembly.Location).ToUpper();
						Process[] p1 = Process.GetProcesses();
						foreach (System.Diagnostics.Process pro in p1)
						{
							try
							{
								if (pro?.ProcessName != null
								   && pro.ProcessName.ToUpper().Contains(applicationName))
								{
									applicationInstanceCount++;
								}
							}
							catch
							{
							}
						}
					}
				}

				return applicationInstanceCount;
			}
		}

		public LibraryConfig Library { get; }

		public ILibraryContext LibraryTabContext { get; private set; }

		public GraphConfig Graph { get; }

		private void InitializeLibrary()
		{
			if (Directory.Exists(ApplicationDataStorage.Instance.DownloadsDirectory))
			{
				this.Library.RegisterContainer(
					new DynamicContainerLink(
						() => "Downloads".Localize(),
						AggContext.StaticData.LoadIcon(Path.Combine("Library", "download_20x20.png")),
						AggContext.StaticData.LoadIcon(Path.Combine("Library", "download_folder.png")),
						() => new FileSystemContainer(ApplicationDataStorage.Instance.DownloadsDirectory)
						{
							UseIncrementedNameDuringTypeChange = true,
							DefaultSort = new SortBehavior()
							{
								SortKey = SortKey.ModifiedDate,
							}
						}));
			}

			this.Library.LibraryCollectionContainer = new LibraryCollectionContainer();

			this.Library.RegisterContainer(
				new DynamicContainerLink(
					() => "Library".Localize(),
					AggContext.StaticData.LoadIcon(Path.Combine("Library", "library_20x20.png")),
					AggContext.StaticData.LoadIcon(Path.Combine("Library", "library_folder.png")),
					() => this.Library.LibraryCollectionContainer));

			if (File.Exists(ApplicationDataStorage.Instance.CustomLibraryFoldersPath))
			{
				// Add each path defined in the CustomLibraryFolders file as a new FileSystemContainerItem
				foreach (string directory in File.ReadLines(ApplicationDataStorage.Instance.CustomLibraryFoldersPath))
				{
					// if (Directory.Exists(directory))
					{
						this.Library.RegisterContainer(
							new FileSystemContainer.DirectoryContainerLink(directory)
							{
								UseIncrementedNameDuringTypeChange = true
							});
					}
				}
			}

			this.Library.PlatingHistory = new PlatingHistoryContainer();

			this.Library.RegisterContainer(
				new DynamicContainerLink(
					() => "History".Localize(),
					AggContext.StaticData.LoadIcon(Path.Combine("Library", "history_20x20.png")),
					AggContext.StaticData.LoadIcon(Path.Combine("Library", "history_folder.png")),
					() => new RootHistoryContainer()));

			// Create a new library context for the SaveAs view
			this.LibraryTabContext = new LibraryConfig()
			{
				ActiveContainer = new WrappedLibraryContainer(this.Library.RootLibaryContainer)
				{
					ExtraContainers = new List<ILibraryContainerLink>()
					{
						new DynamicContainerLink(
							() => "Printers".Localize(),
							AggContext.StaticData.LoadIcon(Path.Combine("Library", "sd_20x20.png")),
							AggContext.StaticData.LoadIcon(Path.Combine("Library", "sd_folder.png")),
							() => new OpenPrintersContainer())
					}
				}
			};
		}

		public void ExportLibraryItems(IEnumerable<ILibraryItem> libraryItems, bool centerOnBed = true, PrinterConfig printer = null)
		{
			UiThread.RunOnIdle(() =>
			{
				if (printer != null || this.ActivePrinters.Count() == 1)
				{
					// If unspecified but count is one, select the one active printer
					if (printer == null)
					{
						printer = this.ActivePrinters.First();
					}

					DialogWindow.Show(
						new ExportPrintItemPage(libraryItems, centerOnBed, printer));
				}
				else
				{
					// If there are no printers setup show the export dialog but have the gcode option disabled
					if (ProfileManager.Instance.ActiveProfiles.Count() == 0)
					{
						DialogWindow.Show(new ExportPrintItemPage(libraryItems, centerOnBed, null));
					}

					// If there is only one printer constructed, use it.
					else if (ProfileManager.Instance.ActiveProfiles.Count() == 1)
					{
						var historyContainer = this.Library.PlatingHistory;

						var printerInfo = ProfileManager.Instance.ActiveProfiles.First();
						ProfileManager.LoadSettingsAsync(printerInfo.ID).ContinueWith(task =>
						{
							var settings = task.Result;
							var onlyPrinter = new PrinterConfig(settings);

							onlyPrinter.Bed.LoadEmptyContent(
								new EditContext()
								{
									ContentStore = historyContainer,
									SourceItem = historyContainer.NewPlatingItem()
								});

							UiThread.RunOnIdle(() =>
							{
								DialogWindow.Show(new ExportPrintItemPage(libraryItems, centerOnBed, onlyPrinter));
							});
						});
					}
					else
					{
						// Resolve printer context before showing export page
						DialogWindow dialogWindow = null;

						dialogWindow = DialogWindow.Show(
							new SelectPrinterProfilePage(
								"Next".Localize(),
								(selectedPrinter) =>
								{
									var historyContainer = this.Library.PlatingHistory;

									selectedPrinter.Bed.LoadEmptyContent(
										new EditContext()
										{
											ContentStore = historyContainer,
											SourceItem = historyContainer.NewPlatingItem()
										});

									dialogWindow.ChangeToPage(
										new ExportPrintItemPage(libraryItems, centerOnBed, selectedPrinter));
								}));
					}
				}
			});
		}

		public ApplicationController()
		{
			this.Thumbnails = new ThumbnailsConfig();

			ProfileManager.UserChanged += (s, e) =>
			{
				// _activePrinters = new List<PrinterConfig>();
			};

			this.BuildSceneOperations();

			this.Extensions = new ExtensionsConfig(this.Library);
			this.Extensions.Register(new ImageEditor());
			this.Extensions.Register(new PublicPropertyEditor());

			HelpArticle helpArticle = null;

			string helpPath = Path.Combine("OEMSettings", "toc.json");
			if (AggContext.StaticData.FileExists(helpPath))
			{
				try
				{
					helpArticle = JsonConvert.DeserializeObject<HelpArticle>(AggContext.StaticData.ReadAllText(helpPath));
				}
				catch { }
			}

			this.HelpArticles = helpArticle ?? new HelpArticle();

			Object3D.AssetsPath = Path.Combine(ApplicationDataStorage.Instance.ApplicationLibraryDataPath, "Assets");

			using (var meshSteam = AggContext.StaticData.OpenStream(Path.Combine("Stls", "missing.stl")))
			{
				Object3D.FileMissingMesh = StlProcessing.Load(meshSteam, CancellationToken.None);
			}

			ScrollBar.DefaultMargin = new BorderDouble(right: 1);
			ScrollBar.ScrollBarWidth = 8 * GuiWidget.DeviceScale;
			ScrollBar.GrowThumbBy = 2;

			// Initialize statics
			DefaultThumbBackground.DefaultBackgroundColor = Color.Transparent;
			Object3D.AssetsPath = ApplicationDataStorage.Instance.LibraryAssetsPath;

			this.Library = new LibraryConfig();
			this.Graph = new GraphConfig(this);
			this.Library.ContentProviders.Add(new[] { "stl", "obj", "amf", "mcx" }, new MeshContentProvider());
			this.Library.ContentProviders.Add("gcode", new GCodeContentProvider());
			this.Library.ContentProviders.Add(new[] { "png", "gif", "jpg", "jpeg" }, new ImageContentProvider());
			this.Library.ContentProviders.Add(new[] { "scad" }, new OpenScadContentProvider());

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "ImageToPath",
					Title = "Image to Path".Localize(),
					MappedTypes = new List<Type> { typeof(ImageObject3D) },
					ResultType = typeof(ImageToPathObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is IObject3D imageObject)
						{
							// TODO: make it look like this (and get rid of all the other stuff)
							// scene.Replace(sceneItem, new ImageToPathObject3D(sceneItem.Clone()));

							var path = new ImageToPathObject3D();

							var itemClone = sceneItem.Clone();
							path.Children.Add(itemClone);
							path.Matrix = itemClone.Matrix;
							itemClone.Matrix = Matrix4X4.Identity;

							scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { path }));
							scene.SelectedItem = null;
							scene.SelectedItem = path;
							path.Invalidate(InvalidateType.Properties);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("noun_479927.png", invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "Translate",
					Title = "Translate".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(TranslateObject3D),
					Operation = (sceneItem, scene) =>
					{
						var items = scene.GetSelectedItems();
						using (new SelectionMaintainer(scene))
						{
							var translate = new TranslateObject3D();
							translate.WrapItems(items, scene.UndoBuffer);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon(Path.Combine("ViewTransformControls", "translate.png"), 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "Rotate",
					Title = "Rotate".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(RotateObject3D_2),
					Operation = (sceneItem, scene) =>
					{
						var items = scene.GetSelectedItems();
						using (new SelectionMaintainer(scene))
						{
							var rotate = new RotateObject3D_2();
							rotate.WrapItems(items, scene.UndoBuffer);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon(Path.Combine("ViewTransformControls", "rotate.png"), 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "Scale",
					Title = "Scale".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(ScaleObject3D),
					Operation = (sceneItem, scene) =>
					{
						var items = scene.GetSelectedItems();
						using (new SelectionMaintainer(scene))
						{
							var scale = new ScaleObject3D();
							scale.WrapItems(items, scene.UndoBuffer);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("scale_32x32.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "ImageConverter",
					Title = "Image Converter".Localize(),
					MappedTypes = new List<Type> { typeof(ImageObject3D) },
					ResultType = typeof(ComponentObject3D),
					Operation = (sceneItem, scene) =>
					{
						var imageObject = sceneItem.Clone() as ImageObject3D;

						var path = new ImageToPathObject3D();
						path.Children.Add(imageObject);

						var smooth = new SmoothPathObject3D();
						smooth.Children.Add(path);

						var extrude = new LinearExtrudeObject3D();
						extrude.Children.Add(smooth);

						var baseObject = new BaseObject3D()
						{
							BaseType = BaseTypes.None
						};
						baseObject.Children.Add(extrude);

						var component = new ComponentObject3D(new[] { baseObject })
						{
							Name = "Image Converter".Localize(),
							ComponentID = "4D9BD8DB-C544-4294-9C08-4195A409217A",
							SurfacedEditors = new List<string>
							{
								"$.Children<BaseObject3D>.Children<LinearExtrudeObject3D>.Children<SmoothPathObject3D>.Children<ImageToPathObject3D>.Children<ImageObject3D>",
								"$.Children<BaseObject3D>.Children<LinearExtrudeObject3D>.Height",
								"$.Children<BaseObject3D>.Children<LinearExtrudeObject3D>.Children<SmoothPathObject3D>.SmoothDistance",
								"$.Children<BaseObject3D>.Children<LinearExtrudeObject3D>.Children<SmoothPathObject3D>.Children<ImageToPathObject3D>",
								"$.Children<BaseObject3D>",
							}
						};

						component.Matrix = imageObject.Matrix;
						imageObject.Matrix = Matrix4X4.Identity;

						using (new SelectionMaintainer(scene))
						{
							scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { component }));
						}                       // Invalidate image to kick off rebuild of ImageConverter stack

						imageObject.Invalidate(InvalidateType.Image);

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("140.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "Mirror",
					Title = "Mirror".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(MirrorObject3D_2),
					Operation = (sceneItem, scene) =>
					{
						var mirror = new MirrorObject3D_2();
						mirror.WrapSelectedItemAndSelect(scene);

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("mirror_32x32.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "MakeComponent",
					Title = "Make Component".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(ComponentObject3D),
					Operation = (sceneItem, scene) =>
					{
						IEnumerable<IObject3D> items = new[] { sceneItem };

						// If SelectionGroup, operate on Children instead
						if (sceneItem is SelectionGroupObject3D)
						{
							items = sceneItem.Children;
						}

						// Dump selection forcing collapse of selection group
						using (new SelectionMaintainer(scene))
						{
							var component = new ComponentObject3D
							{
								Name = "New Component",
								Finalized = false
							};

							// Copy an selected item into the component as a clone
							component.Children.Modify(children =>
							{
								children.AddRange(items.Select(o => o.Clone()));
							});

							component.MakeNameNonColliding();

							scene.UndoBuffer.AddAndDo(new ReplaceCommand(items, new[] { component }));
						}

						return Task.CompletedTask;
					},
					IsVisible = (sceneItem) =>
					{
						return sceneItem.Parent != null
							&& sceneItem.Parent.Parent == null
							&& sceneItem.DescendantsAndSelf().All(d => !(d is ComponentObject3D));
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("scale_32x32.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "EditComponent",
					Title = "Edit Component".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(ComponentObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is ComponentObject3D componentObject)
						{
							// Enable editing mode
							componentObject.Finalized = false;

							// Force editor rebuild
							scene.SelectedItem = null;
							scene.SelectedItem = componentObject;
						}

						return Task.CompletedTask;
					},
					IsVisible = (sceneItem) =>
					{
						return sceneItem.Parent != null
							&& sceneItem.Parent.Parent == null
							&& sceneItem is ComponentObject3D componentObject
							&& componentObject.Finalized
							&& !componentObject.ProOnly;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("scale_32x32.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "LinearExtrude",
					Title = "Linear Extrude".Localize(),
					MappedTypes = new List<Type> { typeof(IPathObject) },
					ResultType = typeof(LinearExtrudeObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is IPathObject imageObject)
						{
							var extrude = new LinearExtrudeObject3D();

							var itemClone = sceneItem.Clone();
							extrude.Children.Add(itemClone);
							extrude.Matrix = itemClone.Matrix;
							itemClone.Matrix = Matrix4X4.Identity;

							using (new SelectionMaintainer(scene))
							{
								scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { extrude }));
							}

							extrude.Invalidate(InvalidateType.Properties);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("noun_84751.png", invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "SmoothPath",
					Title = "Smooth Path".Localize(),
					MappedTypes = new List<Type> { typeof(IPathObject) },
					ResultType = typeof(SmoothPathObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is IPathObject imageObject)
						{
							var smoothPath = new SmoothPathObject3D();
							var itemClone = sceneItem.Clone();
							smoothPath.Children.Add(itemClone);
							smoothPath.Matrix = itemClone.Matrix;
							itemClone.Matrix = Matrix4X4.Identity;

							using (new SelectionMaintainer(scene))
							{
								scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { smoothPath }));
							}

							smoothPath.Invalidate(InvalidateType.Properties);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("noun_simplify_340976_000000.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "InflatePath",
					Title = "Inflate Path".Localize(),
					MappedTypes = new List<Type> { typeof(IPathObject) },
					ResultType = typeof(InflatePathObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is IPathObject imageObject)
						{
							var inflatePath = new InflatePathObject3D();
							var itemClone = sceneItem.Clone();
							inflatePath.Children.Add(itemClone);
							inflatePath.Matrix = itemClone.Matrix;
							itemClone.Matrix = Matrix4X4.Identity;

							using (new SelectionMaintainer(scene))
							{
								scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { inflatePath }));
							}

							inflatePath.Invalidate(InvalidateType.Properties);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("inflate.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "OutlinePath",
					Title = "Outline Path".Localize(),
					MappedTypes = new List<Type> { typeof(IPathObject) },
					ResultType = typeof(OutlinePathObject3D),
					Operation = (sceneItem, scene) =>
					{
						if (sceneItem is IPathObject imageObject)
						{
							var outlinePath = new OutlinePathObject3D();
							var itemClone = sceneItem.Clone();
							outlinePath.Children.Add(itemClone);
							outlinePath.Matrix = itemClone.Matrix;
							itemClone.Matrix = Matrix4X4.Identity;

							using (new SelectionMaintainer(scene))
							{
								scene.UndoBuffer.AddAndDo(new ReplaceCommand(new[] { sceneItem }, new[] { outlinePath }));
							}

							outlinePath.Invalidate(InvalidateType.Properties);
						}

						return Task.CompletedTask;
					},
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("outline.png", 16, 16, invertIcon)
				});

			this.Graph.RegisterOperation(
				new NodeOperation()
				{
					OperationID = "AddBase",
					Title = "Add Base".Localize(),
					MappedTypes = new List<Type> { typeof(IObject3D) },
					ResultType = typeof(BaseObject3D),
					Operation = (item, scene) =>
					{
						bool wasSelected = scene.SelectedItem == item;

						var newChild = item.Clone();
						var baseMesh = new BaseObject3D()
						{
							Matrix = newChild.Matrix
						};
						newChild.Matrix = Matrix4X4.Identity;
						baseMesh.Children.Add(newChild);
						baseMesh.Invalidate(InvalidateType.Properties);

						scene.UndoBuffer.AddAndDo(
							new ReplaceCommand(
								new List<IObject3D> { item },
								new List<IObject3D> { baseMesh }));

						if (wasSelected)
						{
							scene.SelectedItem = baseMesh;
						}

						return Task.CompletedTask;
					},
					IsVisible = (sceneItem) => sceneItem.Children.Any((i) => i is IPathObject),
					IconCollector = (invertIcon) => AggContext.StaticData.LoadIcon("noun_55060.png", invertIcon)
				});

			this.InitializeLibrary();

			this.Graph.PrimaryOperations.Add(typeof(ImageObject3D), new List<NodeOperation> { this.Graph.Operations["ImageConverter"], this.Graph.Operations["ImageToPath"], });
			this.Graph.PrimaryOperations.Add(typeof(ImageToPathObject3D), new List<NodeOperation> { this.Graph.Operations["LinearExtrude"], this.Graph.Operations["SmoothPath"], this.Graph.Operations["InflatePath"] });
			this.Graph.PrimaryOperations.Add(typeof(SmoothPathObject3D), new List<NodeOperation> { this.Graph.Operations["LinearExtrude"], this.Graph.Operations["InflatePath"] });
			this.Graph.PrimaryOperations.Add(typeof(InflatePathObject3D), new List<NodeOperation> { this.Graph.Operations["LinearExtrude"] });
			this.Graph.PrimaryOperations.Add(typeof(OutlinePathObject3D), new List<NodeOperation> { this.Graph.Operations["LinearExtrude"] });
			this.Graph.PrimaryOperations.Add(typeof(Object3D), new List<NodeOperation> { this.Graph.Operations["Scale"] });
		}

		public void Connection_ErrorReported(object sender, string line)
		{
			if (line != null)
			{
				string message = "Your printer is reporting a HARDWARE ERROR and has been paused. Check the error and cancel the print if required.".Localize()
					+ "\n"
					+ "\n"
					+ "Error Reported".Localize() + ":"
					+ $" \"{line}\".";

				if (sender is PrinterConnection printerConnection)
				{
					UiThread.RunOnIdle(() =>
						StyledMessageBox.ShowMessageBox(
							(clickedOk) =>
							{
								if (clickedOk && printerConnection.Paused)
								{
									printerConnection.Resume();
								}
							},
							message,
							"Printer Hardware Error".Localize(),
							StyledMessageBox.MessageType.YES_NO,
							"Resume".Localize(),
							"OK".Localize()));
				}
			}
		}

		public void Connection_TemporarilyHoldingTemp(object sender, EventArgs e)
		{
			if (sender is PrinterConnection printerConnection)
			{
				if (printerConnection.AnyHeatIsOn)
				{
					var paused = false;
					Tasks.Execute("", printerConnection.Printer, (reporter, cancellationToken) =>
					{
						var progressStatus = new ProgressStatus();

						while (printerConnection.SecondsToHoldTemperature > 0
							&& !cancellationToken.IsCancellationRequested
							&& printerConnection.ContinueHoldingTemperature)
						{
							if (paused)
							{
								progressStatus.Status = "Holding Temperature".Localize();
							}
							else
							{
								if (printerConnection.SecondsToHoldTemperature > 60)
								{
									progressStatus.Status = string.Format(
										"{0} {1:0}m {2:0}s",
										"Automatic Heater Shutdown in".Localize(),
										(int)printerConnection.SecondsToHoldTemperature / 60,
										(int)printerConnection.SecondsToHoldTemperature % 60);
								}
								else
								{
									progressStatus.Status = string.Format(
										"{0} {1:0}s",
										"Automatic Heater Shutdown in".Localize(),
										printerConnection.SecondsToHoldTemperature);
								}
							}

							progressStatus.Progress0To1 = printerConnection.SecondsToHoldTemperature / printerConnection.TimeToHoldTemperature;
							reporter.Report(progressStatus);
							Thread.Sleep(20);
						}

						return Task.CompletedTask;
					},
					taskActions: new RunningTaskOptions()
					{
						PauseAction = () => UiThread.RunOnIdle(() =>
						{
							paused = true;
							printerConnection.TimeHaveBeenHoldingTemperature.Stop();
						}),
						PauseToolTip = "Pause automatic heater shutdown".Localize(),
						ResumeAction = () => UiThread.RunOnIdle(() =>
						{
							paused = false;
							printerConnection.TimeHaveBeenHoldingTemperature.Start();
						}),
						ResumeToolTip = "Resume automatic heater shutdown".Localize(),
						StopAction = (abortCancel) => UiThread.RunOnIdle(() =>
						{
							printerConnection.TurnOffBedAndExtruders(TurnOff.Now);
						}),
						StopToolTip = "Immediately turn off heaters".Localize()
					});
				}
			}
		}

		public void Shutdown()
		{
			// Ensure all threads shutdown gracefully on close

			// Release any waiting generator threads
			this.Thumbnails.Shutdown();

			// Kill all long running tasks (this will release the slicing thread if running)
			foreach (var task in Tasks.RunningTasks)
			{
				task.CancelTask();
			}
		}

		private static readonly Dictionary<NamedTypeFace, TypeFace> TypeFaceCache = new Dictionary<NamedTypeFace, TypeFace>()
		{
			[NamedTypeFace.Liberation_Sans] = LiberationSansFont.Instance,
			[NamedTypeFace.Liberation_Sans_Bold] = LiberationSansBoldFont.Instance,
			[NamedTypeFace.Liberation_Mono] = TypeFace.LoadFrom(AggContext.StaticData.ReadAllText(Path.Combine("Fonts", "LiberationMono.svg")))
		};

		private static object locker = new object();

		public static TypeFace GetTypeFace(NamedTypeFace namedTypeFace)
		{
			lock (locker)
			{
				if (!TypeFaceCache.ContainsKey(namedTypeFace))
				{
					TypeFace typeFace = new TypeFace();
					var path = Path.Combine("Fonts", $"{namedTypeFace}.ttf");
					var exists = AggContext.StaticData.FileExists(path);
					var stream = exists ? AggContext.StaticData.OpenStream(path) : null;
					if (stream != null
						&& typeFace.LoadTTF(stream))
					{
						TypeFaceCache.Add(namedTypeFace, typeFace);
					}
					else
					{
						// try the svg
						path = Path.Combine("Fonts", $"{namedTypeFace}.svg");
						exists = AggContext.StaticData.FileExists(path);
						typeFace = exists ? TypeFace.LoadFrom(AggContext.StaticData.ReadAllText(path)) : null;
						if (typeFace != null)
						{
							TypeFaceCache.Add(namedTypeFace, typeFace);
						}
						else
						{
							// assign it to the default
							TypeFaceCache.Add(namedTypeFace, TypeFaceCache[NamedTypeFace.Liberation_Sans]);
						}
					}

					stream?.Dispose();
				}

				return TypeFaceCache[namedTypeFace];
			}
		}

		private static TypeFace titilliumTypeFace = null;

		public static TypeFace TitilliumTypeFace
		{
			get
			{
				if (titilliumTypeFace == null)
				{
					titilliumTypeFace = TypeFace.LoadFrom(AggContext.StaticData.ReadAllText(Path.Combine("Fonts", "TitilliumWeb-Black.svg")));
				}

				return titilliumTypeFace;
			}
		}

		public static string LoadCachedFile(string cacheKey, string cacheScope)
		{
			string cachePath = CacheablePath(cacheScope, cacheKey);

			if (File.Exists(cachePath))
			{
				// Load from cache and deserialize
				return File.ReadAllText(cachePath);
			}

			return null;
		}

		public static Task<T> LoadCacheableAsync<T>(string cacheKey, string cacheScope, string staticDataFallbackPath = null) where T : class
		{
			if (LoadCachedFile(cacheKey, cacheScope) is string cachedFile)
			{
				// Load from cache and deserialize
				return Task.FromResult(
					JsonConvert.DeserializeObject<T>(cachedFile));
			}

			try
			{
				if (staticDataFallbackPath != null
					&& AggContext.StaticData.FileExists(staticDataFallbackPath))
				{
					return Task.FromResult(
						JsonConvert.DeserializeObject<T>(AggContext.StaticData.ReadAllText(staticDataFallbackPath)));
				}
			}
			catch
			{
			}

			return Task.FromResult(default(T));
		}

		/// <summary>
		/// Requests fresh content from online services, falling back to cached content if offline
		/// </summary>
		/// <param name="collector">The custom collector function to load the content</param>
		/// <returns></returns>
		public static async Task<T> LoadCacheableAsync<T>(string cacheKey, string cacheScope, Func<Task<T>> collector, string staticDataFallbackPath = null) where T : class
		{
			string cachePath = CacheablePath(cacheScope, cacheKey);

			try
			{
				// Try to update the document
				T item = await collector();
				if (item != null)
				{
					// update cache on success
					File.WriteAllText(cachePath, JsonConvert.SerializeObject(item, Formatting.Indented));
					return item;
				}
			}
			catch
			{
				// Fall back to preexisting cache if failed
			}

			return await LoadCacheableAsync<T>(cacheKey, cacheScope, staticDataFallbackPath);
		}

		public static string CacheablePath(string cacheScope, string cacheKey)
		{
			string scopeDirectory = Path.Combine(ApplicationDataStorage.Instance.CacheDirectory, cacheScope);

			// Ensure directory exists
			Directory.CreateDirectory(scopeDirectory);

			return Path.Combine(scopeDirectory, cacheKey);
		}

		// Indicates if given file can be opened on the design surface
		public bool IsLoadableFile(string filePath)
		{
			string extension = Path.GetExtension(filePath).ToLower();
			string extensionWithoutPeriod = extension.Trim('.');

			return !string.IsNullOrEmpty(extension)
				&& (ApplicationSettings.OpenDesignFileParams.Contains(extension)
					|| this.Library.ContentProviders.Keys.Contains(extensionWithoutPeriod));
		}

		public bool IsReloading { get; private set; } = false;

		private GuiWidget reloadingOverlay;

		public async Task ReloadAll()
		{
			try
			{
#if DEBUG
				AggContext.StaticData.PurgeCache();
#endif

				this.IsReloading = true;

				reloadingOverlay = new GuiWidget
				{
					HAnchor = HAnchor.Stretch,
					VAnchor = VAnchor.Stretch,
					BackgroundColor = this.Theme.DarkShade
				};

				reloadingOverlay.AddChild(new TextWidget("Reloading".Localize() + "...", textColor: Color.White, pointSize: this.Theme.DefaultFontSize * 1.5)
				{
					HAnchor = HAnchor.Center,
					VAnchor = VAnchor.Center
				});

				AppContext.RootSystemWindow.AddChild(reloadingOverlay);

				await Task.Delay(50);

				GuiWidget.LayoutCount = 0;

				using (new QuickTimer($"ReloadAll_{reloadCount++}:"))
				{
					MainView = new MainViewWidget(this.Theme);
					this.DoneReloadingAll?.CallEvents(null, null);

					using (new QuickTimer("Time to AddMainview: "))
					{
						AppContext.RootSystemWindow.CloseAllChildren();
						AppContext.RootSystemWindow.AddChild(MainView);
					}
				}
			}
			catch (Exception ex)
			{
				reloadingOverlay?.CloseOnIdle();

				UiThread.RunOnIdle(() =>
				{
					StyledMessageBox.ShowMessageBox("An unexpected error occurred during reload".Localize() + ": \n\n" + ex.Message, "Reload Failed".Localize());
				});
			}
			finally
			{
				this.IsReloading = false;
			}

			Debug.WriteLine($"LayoutCount: {GuiWidget.LayoutCount:0.0}");
		}

		private static int reloadCount = 0;

		public void OnApplicationClosed()
		{
			this.Thumbnails.Shutdown();

			ApplicationSettings.Instance.ReleaseClientToken();
		}

		public static ApplicationController Instance
		{
			get
			{
				if (globalInstance == null)
				{
					globalInstance = new ApplicationController();
				}

				return globalInstance;
			}
		}

		public DragDropData DragDropData { get; set; } = new DragDropData();

		public string ShortProductName
		{
			get
			{
				if (this.IsMatterControlPro())
				{
					return "MatterControl Pro";
				}

				return "MatterControl";
			}
		}

		public string ProductName
		{
			get
			{
				if (this.IsMatterControlPro())
				{
					return "MatterControl Pro Edition";
				}

				return "MatterHackers: MatterControl";
			}
		}

		public void SwitchToPurchasedLibrary()
		{
			var purchasedContainer = Library.RootLibaryContainer.ChildContainers.Where(c => c.ID == "LibraryProviderPurchasedKey").FirstOrDefault();
			if (purchasedContainer != null)
			{
				// TODO: Navigate to purchased container
				throw new NotImplementedException("SwitchToPurchasedLibrary");
			}
		}

		public void OnLoadActions()
		{
			if (AssetObject3D.AssetManager == null)
			{
				AssetObject3D.AssetManager = new AssetManager();
			}
		}

		private static void RunSetupIfRequired()
		{
			if (!ProfileManager.Instance.ActiveProfiles.Any())
			{
				// Start the setup wizard if no profiles exist
				UiThread.RunOnIdle(() => DialogWindow.Show(PrinterSetup.GetBestStartPage()));
			}
		}

		public void SwitchToSharedLibrary()
		{
			// Switch to the shared library
			var libraryContainer = Library.RootLibaryContainer.ChildContainers.Where(c => c.ID == "LibraryProviderSharedKey").FirstOrDefault();
			if (libraryContainer != null)
			{
				// TODO: Navigate to purchased container
				throw new NotImplementedException("SwitchToSharedLibrary");
			}
		}

		public void ChangeCloudSyncStatus(bool userAuthenticated, string reason = "")
		{
			UserSettings.Instance.set(UserSettingsKey.CredentialsInvalid, userAuthenticated ? "false" : "true");
			UserSettings.Instance.set(UserSettingsKey.CredentialsInvalidReason, userAuthenticated ? "" : reason);

			CloudSyncStatusChanged.CallEvents(this, new CloudSyncEventArgs() { IsAuthenticated = userAuthenticated });

			if (!string.IsNullOrEmpty(AuthenticationData.Instance.ActiveSessionUsername)
				&& AuthenticationData.Instance.ActiveSessionUsername != AuthenticationData.Instance.LastSessionUsername)
			{
				AuthenticationData.Instance.LastSessionUsername = AuthenticationData.Instance.ActiveSessionUsername;
			}

			// TODO: Unclear why we'd reload on status change - it seems like this state should be managed entirely from ProfileManager and removed from this location
			ProfileManager.ReloadActiveUser();
		}

		public Stream LoadHttpAsset(string url)
		{
			string fingerPrint = ToSHA1(url);
			string cachePath = ApplicationController.CacheablePath("HttpAssets", fingerPrint);

			if (File.Exists(cachePath))
			{
				return File.Open(cachePath, FileMode.Open);
			}
			else
			{
				var client = new WebClient();
				var bytes = client.DownloadData(url);

				File.WriteAllBytes(cachePath, bytes);

				return new MemoryStream(bytes);
			}
		}

		public async Task<PrinterConfig> LoadPrinter(string printerID)
		{
			var printer = this.ActivePrinters.FirstOrDefault(p => p.Settings.ID == printerID);
			if (printer == null)
			{
				if (!string.IsNullOrEmpty(printerID)
					&& ProfileManager.Instance[printerID] != null)
				{
					printer = new PrinterConfig(await ProfileManager.LoadSettingsAsync(printerID));
				}
			}

			if (printer != null
				&& printer.Settings.PrinterSelected
				&& printer.Settings.GetValue<bool>(SettingsKey.auto_connect))
			{
				printer.Connection.Connect();
			}

			return printer;
		}

		public async Task<PrinterConfig> OpenEmptyPrinter(string printerID)
		{
			PartWorkspace workspace = null;

			if (!string.IsNullOrEmpty(printerID)
				&& ProfileManager.Instance[printerID] != null)
			{
				var printer = await this.LoadPrinter(printerID);

				// Add workspace for printer
				workspace = new PartWorkspace(printer);

				var history = this.Library.PlatingHistory;

				await workspace.SceneContext.LoadContent(new EditContext()
				{
					ContentStore = history,
					SourceItem = history.NewPlatingItem()
				});

				if (workspace.Printer != null)
				{
					workspace.Name = workspace.Printer.Settings.GetValue(SettingsKey.printer_name);
				}

				this.OpenWorkspace(workspace);

				return printer;
			}

			return null;
		}

		public void OpenPrinter(PrinterInfo printerInfo)
		{
			if (this.ActivePrinters.FirstOrDefault(p => p.Settings.ID == printerInfo.ID) is PrinterConfig printer
				&& this.MainView.TabControl.AllTabs.FirstOrDefault(t => t.TabContent is PrinterTabPage printerTabPage && printerTabPage.printer == printer) is ITab tab)
			{
				// Switch to existing printer tab
				this.MainView.TabControl.ActiveTab = tab;
			}
			else
			{
				// Open new printer tab
				this.OpenEmptyPrinter(printerInfo.ID).ConfigureAwait(false);
			}
		}

		public void OpenWorkspace(PartWorkspace workspace)
		{
			this.OpenWorkspace(workspace, WorkspacesChangedEventArgs.OperationType.Add);
		}

		private void OpenWorkspace(PartWorkspace workspace, WorkspacesChangedEventArgs.OperationType operationType)
		{
			this.OnWorkspacesChanged(
					new WorkspacesChangedEventArgs(
						workspace,
						operationType));

			this.Workspaces.Add(workspace);
		}

		public void RestoreWorkspace(PartWorkspace workspace)
		{
			this.OpenWorkspace(workspace, WorkspacesChangedEventArgs.OperationType.Restore);
		}

		private string loadedUserTabs = null;

		public async Task RestoreUserTabs()
		{
			// Prevent reload of loaded user
			if (loadedUserTabs == ProfileManager.Instance.UserName)
			{
				return;
			}

			loadedUserTabs = ProfileManager.Instance.UserName;

			var history = this.Library.PlatingHistory;

			this.Workspaces.Clear();

			if (File.Exists(ProfileManager.Instance.OpenTabsPath))
			{
				try
				{
					string openTabsText = File.ReadAllText(ProfileManager.Instance.OpenTabsPath);
					var persistedWorkspaces = JsonConvert.DeserializeObject<List<PartWorkspace>>(
						openTabsText,
						new ContentStoreConverter(),
						new LibraryItemConverter());

					var loadedPrinters = new HashSet<string>();

					foreach (var persistedWorkspace in persistedWorkspaces)
					{
						try
						{
							// Load the actual workspace if content file exists
							if (File.Exists(persistedWorkspace.ContentPath))
							{
								string printerID = persistedWorkspace.PrinterID;

								PartWorkspace workspace = null;

								if (!string.IsNullOrEmpty(printerID)
									&& ProfileManager.Instance[printerID] != null)
								{
									// Only create one workspace per printer
									if (!loadedPrinters.Contains(printerID))
									{
										// Add workspace for printer
										workspace = new PartWorkspace(await this.LoadPrinter(persistedWorkspace.PrinterID));

										loadedPrinters.Add(printerID);
									}
									else
									{
										// Ignore additional workspaces for the same printer once one is loaded
										continue;
									}
								}
								else
								{
									// Add workspace for part
									workspace = new PartWorkspace(new BedConfig(history));
								}

								// Load the previous content
								await workspace.SceneContext.LoadContent(new EditContext()
								{
									ContentStore = history,
									SourceItem = new FileSystemFileItem(persistedWorkspace.ContentPath)
								});

								if (workspace.Printer != null)
								{
									workspace.Name = workspace.Printer.Settings.GetValue(SettingsKey.printer_name);
								}
								else
								{
									workspace.Name = workspace?.SceneContext.EditContext?.SourceItem?.Name ?? "Unknown";
								}

								this.RestoreWorkspace(workspace);
							}
						}
						catch
						{
							// Suppress workspace load exceptions and continue to the next workspace
						}
					}
				}
				catch
				{
					// Suppress deserialization issues with opentabs.json and continue with an empty Workspaces lists
				}
			}

			if (this.Workspaces.Count == 0)
			{
				var workspace = new PartWorkspace(new BedConfig(history))
				{
					Name = "New Design".Localize()
				};

				// Load it up
				workspace.SceneContext.LoadEmptyContent(
					new EditContext()
					{
						ContentStore = history,
						SourceItem = history.NewPlatingItem()
					});

				ApplicationController.Instance.MainTabKey = workspace.Name;

				// Open but no need to save
				this.OpenWorkspace(workspace, WorkspacesChangedEventArgs.OperationType.Restore);
			}
		}

		/// <summary>
		/// Compute hash for string encoded as UTF8
		/// </summary>
		/// <param name="s">String to be hashed</param>
		public static string ToSHA1(string s)
		{
			byte[] bytes = Encoding.UTF8.GetBytes(s);

			// var timer = Stopwatch.StartNew();
			using (var sha1 = System.Security.Cryptography.SHA1.Create())
			{
				byte[] hash = sha1.ComputeHash(bytes);
				string SHA1 = BitConverter.ToString(hash).Replace("-", string.Empty);

				// Console.WriteLine("{0} {1} {2}", SHA1, timer.ElapsedMilliseconds, filePath);
				return SHA1;
			}
		}

		/// <summary>
		/// Register the given PrintItemAction into the named section
		/// </summary>
		/// <param name="section">The section to register in</param>
		/// <param name="printItemAction">The action to register</param>
		public void RegisterLibraryAction(string section, LibraryAction printItemAction)
		{
			if (!registeredLibraryActions.TryGetValue(section, out List<LibraryAction> items))
			{
				items = new List<LibraryAction>();
				registeredLibraryActions.Add(section, items);
			}

			items.Add(printItemAction);
		}

		/// <summary>
		/// Register the given SceneSelectionOperation
		/// </summary>
		/// <param name="operation">The action to register</param>
		public void RegisterSceneOperation(SceneSelectionOperation operation)
		{
			if (operation.OperationType != null)
			{
				this.Thumbnails.OperationIcons.Add(operation.OperationType, operation.Icon);
			}

			registeredSceneOperations.Add(operation);
		}

		/// <summary>
		/// Enumerate the given section, returning all registered actions
		/// </summary>
		/// <param name="section">The section to enumerate</param>
		/// <returns></returns>
		public IEnumerable<LibraryAction> RegisteredLibraryActions(string section)
		{
			if (registeredLibraryActions.TryGetValue(section, out List<LibraryAction> items))
			{
				return items;
			}

			return Enumerable.Empty<LibraryAction>();
		}

		public IEnumerable<SceneSelectionOperation> RegisteredSceneOperations => registeredSceneOperations;

		public static IObject3D ClipboardItem { get; internal set; }

		public Action<ILibraryItem> ShareLibraryItem { get; set; }

		public List<PartWorkspace> Workspaces { get; } = new List<PartWorkspace>();

		public AppViewState ViewState { get; } = new AppViewState();

		public Uri HelpArticleSource { get; set; }

		public Dictionary<string, HelpArticle> HelpArticlesByID { get; set; }

		public string MainTabKey
		{
			get => UserSettings.Instance.get(UserSettingsKey.MainTabKey);
			set => UserSettings.Instance.set(UserSettingsKey.MainTabKey, value);
		}

		public static List<StartupAction> StartupActions { get; } = new List<StartupAction>();

		public static List<StartupTask> StartupTasks { get; } = new List<StartupTask>();

		public static Type ServicesStatusType { get; set; }

		/// <summary>
		/// Gets a value indicating whether any ActivePrinter is running a print task, either in paused or printing states
		/// </summary>
		public bool AnyPrintTaskRunning => this.ActivePrinters.Any(p => p.Connection.Printing || p.Connection.Paused || p.Connection.CommunicationState == CommunicationStates.PreparingToPrint);

		private List<TourLocation> _productTour;

		public async Task<List<TourLocation>> LoadProductTour()
		{
			if (_productTour == null)
			{
				_productTour = await ApplicationController.LoadCacheableAsync<List<TourLocation>>(
					"ProductTour.json",
					"MatterHackers",
					async () =>
					{
						var httpClient = new HttpClient();
						string json = await httpClient.GetStringAsync("https://matterhackers.github.io/MatterControl-Help/docs/product-tour.json");
						// string json = await httpClient.GetStringAsync("https://matterhackers.github.io/MatterControl-Docs/Help/product-tour.json");

						return JsonConvert.DeserializeObject<List<TourLocation>>(json);
					},
					Path.Combine("OemSettings", "ProductTour.json"));
			}

			return _productTour;
		}

		public event EventHandler<ApplicationTopBarCreatedEventArgs> ApplicationTopBarCreated;

		public void NotifyPrintersTabRightElement(GuiWidget sourceExentionArea)
		{
			ApplicationTopBarCreated?.Invoke(this, new ApplicationTopBarCreatedEventArgs(sourceExentionArea));

			// after adding content to the right side make sure we hold the space in the tab bar
			var leftChild = sourceExentionArea.Parent.Children.First();
			var padding = leftChild.Padding;
			leftChild.Padding = new BorderDouble(padding.Left, padding.Bottom, sourceExentionArea.Width, padding.Height);
		}

		public async Task PrintPart(EditContext editContext, PrinterConfig printer, IProgress<ProgressStatus> reporter, CancellationToken cancellationToken)
		{
			var partFilePath = editContext.SourceFilePath;
			var gcodeFilePath = editContext.GCodeFilePath(printer);
			var printItemName = editContext.SourceItem.Name;

			// Exit if called in a non-applicable state
			if (printer.Connection.CommunicationState != CommunicationStates.Connected
				&& printer.Connection.CommunicationState != CommunicationStates.FinishedPrint)
			{
				return;
			}

			try
			{
				if (PrinterCalibrationWizard.SetupRequired(printer, requiresLoadedFilament: true))
				{
					UiThread.RunOnIdle(() =>
					{
						DialogWindow.Show(
							new PrinterCalibrationWizard(printer, AppContext.Theme),
							advanceToIncompleteStage: true);
					});

					return;
				}

				printer.Connection.PrintingItemName = printItemName;

				var errors = printer.ValidateSettings(validatePrintBed: !printer.Bed.EditContext.IsGGCodeSource);
				if (errors.Any(e => e.ErrorLevel == ValidationErrorLevel.Error))
				{
					this.ShowValidationErrors("Validation Error".Localize(), errors);
				}
				else // there are no errors continue printing
				{
					// clear the output cache prior to starting a print
					printer.Connection.TerminalLog.Clear();

					string hideGCodeWarning = ApplicationSettings.Instance.get(ApplicationSettingsKey.HideGCodeWarning);

					if (Path.GetExtension(partFilePath).ToUpper() == ".GCODE")
					{
						if (hideGCodeWarning != "true")
						{
							var hideGCodeWarningCheckBox = new CheckBox("Don't remind me again".Localize())
							{
								TextColor = this.Theme.TextColor,
								Margin = new BorderDouble(top: 6, left: 6),
								HAnchor = Agg.UI.HAnchor.Left
							};
							hideGCodeWarningCheckBox.Click += (sender, e) =>
							{
								if (hideGCodeWarningCheckBox.Checked)
								{
									ApplicationSettings.Instance.set(ApplicationSettingsKey.HideGCodeWarning, "true");
								}
								else
								{
									ApplicationSettings.Instance.set(ApplicationSettingsKey.HideGCodeWarning, null);
								}
							};

							UiThread.RunOnIdle(() =>
							{
								StyledMessageBox.ShowMessageBox(
									(messageBoxResponse) =>
									{
										if (messageBoxResponse)
										{
											printer.Connection.CommunicationState = CommunicationStates.PreparingToPrint;
											this.ArchiveAndStartPrint(partFilePath, gcodeFilePath, printer);
										}
									},
									"The file you are attempting to print is a GCode file.\n\nIt is recommended that you only print Gcode files known to match your printer's configuration.\n\nAre you sure you want to print this GCode file?".Localize(),
									"Warning - GCode file".Localize(),
									new GuiWidget[]
									{
									hideGCodeWarningCheckBox
									},
									StyledMessageBox.MessageType.YES_NO);
							});
						}
						else
						{
							printer.Connection.CommunicationState = CommunicationStates.PreparingToPrint;
							this.ArchiveAndStartPrint(partFilePath, gcodeFilePath, printer);
						}
					}
					else
					{
						printer.Connection.CommunicationState = CommunicationStates.PreparingToPrint;

						(bool slicingSucceeded, string finalPath) = await this.SliceItemLoadOutput(
							printer,
							printer.Bed.Scene,
							gcodeFilePath);

						// Only start print if slicing completed
						if (slicingSucceeded)
						{
							this.ArchiveAndStartPrint(partFilePath, finalPath, printer);
						}
						else
						{
							// TODO: Need to reset printing state? This seems like I shouldn't own this indicator
							printer.Connection.CommunicationState = CommunicationStates.Connected;
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}

		public void ShowValidationErrors(string windowTitle, List<ValidationError> errors)
		{
			UiThread.RunOnIdle(() =>
			{
				var dialogPage = new DialogPage("Close".Localize())
				{
					HAnchor = HAnchor.Stretch,
					WindowTitle = windowTitle,
					HeaderText = "Action Required".Localize()
				};

				dialogPage.ContentRow.AddChild(new ValidationErrorsPanel(errors, AppContext.Theme)
				{
					HAnchor = HAnchor.Stretch
				});

				DialogWindow.Show(dialogPage);
			});
		}

		public void ResetTranslationMap()
		{
			LoadTranslationMap();
		}

		public static void LoadTranslationMap()
		{
			// Select either the user supplied language name or the current thread language name
			string twoLetterIsoLanguageName = string.IsNullOrEmpty(UserSettings.Instance.Language) ?
				Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName.ToLower() :
				UserSettings.Instance.Language.ToLower();

			if (twoLetterIsoLanguageName == "ja"
				|| twoLetterIsoLanguageName == "zh")
			{
				AggContext.DefaultFont = ApplicationController.GetTypeFace(NamedTypeFace.Firefly_Sung);
				AggContext.DefaultFontBold = ApplicationController.GetTypeFace(NamedTypeFace.Firefly_Sung);
				AggContext.DefaultFontItalic = ApplicationController.GetTypeFace(NamedTypeFace.Firefly_Sung);
				AggContext.DefaultFontBoldItalic = ApplicationController.GetTypeFace(NamedTypeFace.Firefly_Sung);
			}
			else
			{
				AggContext.DefaultFont = LiberationSansFont.Instance;
				AggContext.DefaultFontBold = LiberationSansBoldFont.Instance;
				AggContext.DefaultFontItalic = LiberationSansFont.Instance;
				AggContext.DefaultFontBoldItalic = LiberationSansBoldFont.Instance;
			}

			string translationFilePath = Path.Combine("Translations", twoLetterIsoLanguageName, "Translation.txt");

			if (twoLetterIsoLanguageName == "en")
			{
				TranslationMap.ActiveTranslationMap = new TranslationMap();
			}
			else
			{
				using (var stream = AggContext.StaticData.OpenStream(translationFilePath))
				using (var streamReader = new StreamReader(stream))
				{
					TranslationMap.ActiveTranslationMap = new TranslationMap(streamReader, UserSettings.Instance.Language);
				}
			}
		}

		public void MonitorPrintTask(PrinterConfig printer)
		{
			string layerDetails = (printer.Bed.LoadedGCode?.LayerCount > 0) ? $" of {printer.Bed.LoadedGCode.LayerCount}" : "";

			this.Tasks.Execute(
				"Printing".Localize(),
				printer,
				(reporterB, cancellationTokenB) =>
				{
					var progressStatus = new ProgressStatus();
					reporterB.Report(progressStatus);

					return Task.Run(() =>
					{
						string printing = "Printing".Localize();
						int totalLayers = printer.Connection.TotalLayersInPrint;

						while (!printer.Connection.Printing
							&& !cancellationTokenB.IsCancellationRequested)
						{
							// Wait for printing
							Thread.Sleep(200);
						}

						while ((printer.Connection.Printing || printer.Connection.Paused)
							&& !cancellationTokenB.IsCancellationRequested)
						{
							progressStatus.Status = $"{printing} ({printer.Connection.CurrentlyPrintingLayer + 1}{layerDetails}) - {printer.Connection.PercentComplete:0}%";

							progressStatus.Progress0To1 = printer.Connection.PercentComplete / 100;
							reporterB.Report(progressStatus);
							Thread.Sleep(200);
						}
					});
				},
				taskActions: new RunningTaskOptions()
				{
					ExpansionSerializationKey = $"{nameof(MonitorPrintTask)}_expanded",
					RichProgressWidget = () => PrinterTabPage.PrintProgressWidget(printer, this.Theme),
					PauseAction = () => UiThread.RunOnIdle(() =>
					{
						printer.Connection.RequestPause();
					}),
					IsPaused = () =>
					{
						return printer.Connection.Paused;
					},
					PauseToolTip = "Pause Print".Localize(),
					ResumeAction = () => UiThread.RunOnIdle(() =>
					{
						printer.Connection.Resume();
					}),
					ResumeToolTip = "Resume Print".Localize(),
					StopAction = (abortCancel) => UiThread.RunOnIdle(() =>
					{
						printer.CancelPrint(abortCancel);
					}),
					StopToolTip = "Cancel Print".Localize(),
				});
		}

		private static PluginManager pluginManager = null;

		public static PluginManager Plugins
		{
			get
			{
				// PluginManager initialization must occur late, after the config is loaded and after localization libraries
				// have occurred, which currently is driven by MatterControlApplication init
				if (pluginManager == null)
				{
					pluginManager = new PluginManager();
				}

				return pluginManager;
			}
		}

		public bool Allow32BitReSlice { get; set; }

		/// <summary>
		/// Archives MCX and validates GCode results before starting a print operation
		/// </summary>
		/// <param name="sourcePath">The source file which originally caused the slice->print operation</param>
		/// <param name="gcodeFilePath">The resulting GCode to print</param>
		private async void ArchiveAndStartPrint(string sourcePath, string gcodeFilePath, PrinterConfig printer)
		{
			if (File.Exists(sourcePath)
				&& File.Exists(gcodeFilePath))
			{
				bool originalIsGCode = Path.GetExtension(sourcePath).ToUpper() == ".GCODE";
				if (File.Exists(gcodeFilePath))
				{
					// Create archive point for printing attempt
					if (Path.GetExtension(sourcePath).ToUpper() == ".MCX")
					{
						string now = "Workspace " + DateTime.Now.ToString("yyyy-MM-dd HH_mm_ss");
						string archivePath = Path.Combine(ApplicationDataStorage.Instance.PrintHistoryPath, now + ".zip");

						string settingsFilePath = ProfileManager.Instance.ProfilePath(printer.Settings.ID);

						using (var file = File.OpenWrite(archivePath))
						using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
						{
							zip.CreateEntryFromFile(sourcePath, "PrinterPlate.mcx");
							zip.CreateEntryFromFile(settingsFilePath, printer.Settings.GetValue(SettingsKey.printer_name) + ".printer");
							zip.CreateEntryFromFile(gcodeFilePath, "sliced.gcode");
						}
					}

					if (originalIsGCode)
					{
						await printer.Connection.StartPrint(gcodeFilePath);

						MonitorPrintTask(printer);

						return;
					}
					else
					{
						// Ask for slicer specific gcode validation
						if (printer.Settings.Slicer.ValidateFile(gcodeFilePath))
						{
							await printer.Connection.StartPrint(gcodeFilePath);
							MonitorPrintTask(printer);
							return;
						}
					}
				}

				printer.Connection.CommunicationState = CommunicationStates.Connected;
			}
		}

		/// <summary>
		/// Slice the given IObject3D to the target GCode file using the referenced printer settings
		/// </summary>
		/// <param name="printer">The printer/settings to use</param>
		/// <param name="object3D">The IObject3D to slice</param>
		/// <param name="gcodeFilePath">The path to write the file to</param>
		/// <returns>A boolean indicating if the slicing operation completed without aborting</returns>
		public async Task<(bool, string)> SliceItemLoadOutput(PrinterConfig printer, IObject3D object3D, string gcodeFilePath)
		{
			// Slice
			bool slicingSucceeded = false;

			printer.ViewState.SlicingItem = true;

			await this.Tasks.Execute("Slicing".Localize(), printer, async (reporter, cancellationToken) =>
			{
				slicingSucceeded = await Slicer.SliceItem(
					object3D,
					gcodeFilePath,
					printer,
					reporter,
					cancellationToken);
			});

			printer.ViewState.SlicingItem = false;

			// Skip loading GCode output if slicing failed
			if (!slicingSucceeded)
			{
				return (false, gcodeFilePath);
			}

			var postProcessors = printer.Bed.Scene.Children.OfType<IGCodePostProcessor>();
			if (postProcessors.Any())
			{
				using (var resultStream = File.OpenRead(gcodeFilePath))
				{
					Stream contextStream = resultStream;

					// Execute each post processor
					foreach (var processor in postProcessors)
					{
						// Invoke the processor and store the resulting output to the context stream reference
						contextStream = processor.ProcessOutput(contextStream);

						// Reset to the beginning
						contextStream.Position = 0;
					}

					// Modify final file name
					gcodeFilePath = Path.ChangeExtension(gcodeFilePath, GCodeFile.PostProcessedExtension);

					// Copy the final stream to the revised gcodeFilePath
					using (var finalStream = File.OpenWrite(gcodeFilePath))
					{
						contextStream.CopyTo(finalStream);
					}
				}
			}

			await this.Tasks.Execute("Loading GCode".Localize(), printer, (innerProgress, token) =>
			{
				var status = new ProgressStatus();

				innerProgress.Report(status);

				printer.Bed.LoadActiveSceneGCode(gcodeFilePath, token, (progress0to1, statusText) =>
				{
					UiThread.RunOnIdle(() =>
					{
						status.Progress0To1 = progress0to1;
						status.Status = statusText;

						innerProgress.Report(status);
					});
				});

				if (printer.Bed.LoadedGCode is GCodeMemoryFile gcodeMemoryFile)
				{
					// try to validate the gcode file and warn if it seems invalid.
					// for now the definition of invalid is that it has a print time of < 30 seconds
					var estimatedPrintSeconds = gcodeMemoryFile.EstimatedPrintSeconds();
					if (estimatedPrintSeconds < 30)
					{
						var message = "The time to print this G-Code is estimated to be {0} seconds.\n\nPlease check your part for errors if this is unexpected."
							.Localize()
							.FormatWith((int)estimatedPrintSeconds);
						UiThread.RunOnIdle(() =>
						{
							StyledMessageBox.ShowMessageBox(message, "Warning, very short print".Localize());
						});
					}
				}

				// Switch to the 3D layer view if on Model view and slicing succeeded
				if (printer.ViewState.ViewMode == PartViewMode.Model)
				{
					printer.ViewState.ViewMode = PartViewMode.Layers3D;
				}

				return Task.CompletedTask;
			});

			return (slicingSucceeded, gcodeFilePath);
		}

		internal void GetViewOptionButtons(GuiWidget parent, ISceneContext sceneContext, PrinterConfig printer, ThemeConfig theme)
		{
			var bedButton = new RadioIconButton(AggContext.StaticData.LoadIcon("bed.png", theme.InvertIcons), theme)
			{
				Name = "Bed Button",
				ToolTipText = "Show Print Bed".Localize(),
				Checked = sceneContext.RendererOptions.RenderBed,
				Margin = theme.ButtonSpacing,
				VAnchor = VAnchor.Absolute,
				ToggleButton = true,
				Height = theme.ButtonHeight,
				Width = theme.ButtonHeight,
				SiblingRadioButtonList = new List<GuiWidget>()
			};
			bedButton.CheckedStateChanged += (s, e) =>
			{
				sceneContext.RendererOptions.RenderBed = bedButton.Checked;
			};
			parent.AddChild(bedButton);

			Func<bool> buildHeightValid = () => sceneContext.BuildHeight > 0;

			var printAreaButton = new RadioIconButton(AggContext.StaticData.LoadIcon("print_area.png", theme.InvertIcons), theme)
			{
				Name = "Bed Button",
				ToolTipText = buildHeightValid() ? "Show Print Area".Localize() : "Define printer build height to enable",
				Checked = sceneContext.RendererOptions.RenderBuildVolume,
				Margin = theme.ButtonSpacing,
				VAnchor = VAnchor.Absolute,
				ToggleButton = true,
				Enabled = buildHeightValid() && printer?.ViewState.ViewMode != PartViewMode.Layers2D,
				Height = theme.ButtonHeight,
				Width = theme.ButtonHeight,
				SiblingRadioButtonList = new List<GuiWidget>()
			};
			printAreaButton.CheckedStateChanged += (s, e) =>
			{
				sceneContext.RendererOptions.RenderBuildVolume = printAreaButton.Checked;
			};
			parent.AddChild(printAreaButton);

			this.BindBedOptions(parent, bedButton, printAreaButton, sceneContext.RendererOptions);

			if (printer != null)
			{
				// Disable print area button in GCode2D view
				EventHandler<ViewModeChangedEventArgs> viewModeChanged = (s, e) =>
				{
					// Button is conditionally created based on BuildHeight, only set enabled if created
					printAreaButton.Enabled = buildHeightValid() && printer.ViewState.ViewMode != PartViewMode.Layers2D;
				};

				printer.ViewState.ViewModeChanged += viewModeChanged;

				parent.Closed += (s, e) =>
				{
					printer.ViewState.ViewModeChanged -= viewModeChanged;
				};
			}
		}

		public void BindBedOptions(GuiWidget container, ICheckbox bedButton, ICheckbox printAreaButton, View3DConfig renderOptions)
		{
			PropertyChangedEventHandler syncProperties = (s, e) =>
			{
				switch (e.PropertyName)
				{
					case nameof(renderOptions.RenderBed):
						bedButton.Checked = renderOptions.RenderBed;
						break;

					case nameof(renderOptions.RenderBuildVolume) when printAreaButton != null:
						printAreaButton.Checked = renderOptions.RenderBuildVolume;
						break;
				}
			};

			renderOptions.PropertyChanged += syncProperties;

			container.Closed += (s, e) =>
			{
				renderOptions.PropertyChanged -= syncProperties;
			};
		}

		public void ShellOpenFile(string file)
		{
			UiThread.RunOnIdle(() => this.ShellFileOpened?.Invoke(this, file));
		}

		public void Connection_PrintFinished(object sender, string e)
		{
			if (sender is PrinterConnection printerConnection
				&& !printerConnection.CalibrationPrint)
			{
				// show a long running task asking about print feedback and up-selling more materials
				// Ask about the print, offer help if needed.
				// Let us know how your print came out.
				string markdownText = @"**Find more at MatterHackers**

Supplies and accessories:
- [Filament](https://www.matterhackers.com/store/c/3d-printer-filament)
- [Bed Adhesives](https://www.matterhackers.com/store/c/3d-printer-adhesive)
- [Digital Designs](https://www.matterhackers.com/store/c/digital-designs)

Support and tutorials:
- [MatterControl Docs](https://www.matterhackers.com/mattercontrol/support)
- [Tutorials](https://www.matterhackers.com/store/l/mattercontrol/sk/MKZGTDW6#tutorials)
- [Trick, Tips & Support Articles](https://www.matterhackers.com/support#mattercontrol)
- [User Forum](https://forums.matterhackers.com/recent)";

				var time = Stopwatch.StartNew();
				ShowNotification("Congratulations Print Complete".Localize(), markdownText, UserSettingsKey.ShownPrintCompleteMessage);
			}
		}

		public void Connection_PrintCanceled(object sender, EventArgs e)
		{
			if (sender is PrinterConnection printerConnection
				&& !printerConnection.CalibrationPrint)
			{
				// show a long running task showing support options
				// add links to forum, articles and documentation
				// support: "https://www.matterhackers.com/support#mattercontrol"
				// documentation: "https://www.matterhackers.com/mattercontrol/support"
				// forum: "https://forums.matterhackers.com/recent"

				string markdownText = @"Looks like you canceled this print. If you need help, here are some links that might be useful.
- [MatterControl Docs](https://www.matterhackers.com/mattercontrol/support)
- [Tutorials](https://www.matterhackers.com/store/l/mattercontrol/sk/MKZGTDW6#tutorials)
- [Trick, Tips & Support Articles](https://www.matterhackers.com/support#mattercontrol)
- [User Forum](https://forums.matterhackers.com/recent)";

				var time = Stopwatch.StartNew();
				ShowNotification("Print Canceled".Localize(), markdownText, UserSettingsKey.ShownPrintCanceledMessage);
			}
		}

		private void ShowNotification(string title, string markdownText, string userKey)
		{
			var hideAfterPrintMessage = new CheckBox("Don't show this again".Localize())
			{
				TextColor = AppContext.Theme.TextColor,
				Margin = new BorderDouble(top: 6, left: 6),
				HAnchor = Agg.UI.HAnchor.Left,
				Checked = UserSettings.Instance.get(userKey) == "false",
			};
			hideAfterPrintMessage.Click += (s, e1) =>
			{
				if (hideAfterPrintMessage.Checked)
				{
					UserSettings.Instance.set(userKey, "false");
				}
				else
				{
					UserSettings.Instance.set(userKey, "true");
				}
			};

			if (!hideAfterPrintMessage.Checked
				&& !string.IsNullOrEmpty(markdownText))
			{
				UiThread.RunOnIdle(() =>
				{
					StyledMessageBox.ShowMessageBox(null,
						markdownText,
						title,
						new[] { hideAfterPrintMessage },
						StyledMessageBox.MessageType.OK,
						useMarkdown: true);
				});
			}
		}

		public void ConnectToPrinter(PrinterConfig printer)
		{
			if (!printer.Settings.PrinterSelected)
			{
				return;
			}

			bool listenForConnectFailed = true;
			long connectStartMs = UiThread.CurrentTimerMs;

			void Connection_Failed(object s, EventArgs e)
			{
#if !__ANDROID__
				// TODO: Someday this functionality should be revised to an awaitable Connect() call in the Connect button that
				// shows troubleshooting on failed attempts, rather than hooking the failed event and trying to determine if the
				// Connect button started the task
				if (listenForConnectFailed
					&& UiThread.CurrentTimerMs - connectStartMs < 25000)
				{
					UiThread.RunOnIdle(() =>
					{
						// User initiated connect attempt failed, show port selection dialog
						DialogWindow.Show(new SetupStepComPortOne(printer));
					});
				}
#endif
				ClearEvents();
			}

			void Connection_Succeeded(object s, EventArgs e)
			{
				ClearEvents();
			}

			void ClearEvents()
			{
				listenForConnectFailed = false;

				printer.Connection.ConnectionFailed -= Connection_Failed;
				printer.Connection.ConnectionSucceeded -= Connection_Succeeded;
			}

			printer.Connection.ConnectionFailed += Connection_Failed;
			printer.Connection.ConnectionSucceeded += Connection_Succeeded;

			if (AppContext.Platform.HasPermissionToDevice(printer))
			{
				printer.Connection.HaltConnectionThread();
				printer.Connection.Connect();
			}
		}

		/// <summary>
		/// Replace invalid filename characters with the given replacement value to ensure working paths for the current filesystem
		/// </summary>
		/// <param name="name">The filename name to consider</param>
		/// <param name="replacementCharacter">The replacement character to use</param>
		/// <returns>A sanitized file name that is safe to use on the current system</returns>
		public string SanitizeFileName(string name, string replacementCharacter = "_")
		{
			if (string.IsNullOrEmpty(name))
			{
				return name;
			}

			string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
			string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);

			return Regex.Replace(name, invalidRegStr, replacementCharacter);
		}

		public ChromeTab ActivateHelpTab()
		{
			var tabControl = this.MainView.TabControl;
			var theme = AppContext.Theme;

			var helpDocsTab = tabControl.AllTabs.FirstOrDefault(t => t.Key == "HelpDocs") as ChromeTab;
			if (helpDocsTab == null)
			{
				var helpTreePanel = new HelpTreePanel(theme)
				{
					HAnchor = HAnchor.Stretch,
					VAnchor = VAnchor.Stretch
				};

				var icon = AggContext.StaticData.LoadIcon("help_page.png", 16, 16, theme.InvertIcons);

				helpDocsTab = new ChromeTab("HelpDocs", "Help".Localize(), tabControl, helpTreePanel, theme, icon)
				{
					MinimumSize = new Vector2(0, theme.TabButtonHeight),
					Name = "Help Tab",
				};

				tabControl.AddTab(helpDocsTab);
			}

			tabControl.ActiveTab = helpDocsTab;

			return helpDocsTab;
		}

		public class CloudSyncEventArgs : EventArgs
		{
			public bool IsAuthenticated { get; set; }
		}

		public class StartupTask
		{
			public string Title { get; set; }

			public int Priority { get; set; }

			public Func<IProgress<ProgressStatus>, CancellationToken, Task> Action { get; set; }
		}

		public class StartupAction
		{
			public string Title { get; set; }

			public int Priority { get; set; }

			public Action Action { get; set; }
		}
	}

	public enum ReportSeverity2
	{
		Warning,
		Error
	}
}