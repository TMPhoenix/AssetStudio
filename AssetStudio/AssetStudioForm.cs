﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.IO;
using System.Threading;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing.Text;
using AssetStudio.Properties;
using FMOD;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using static AssetStudio.Studio;
using static AssetStudio.Importer;

namespace AssetStudio
{
	partial class AssetStudioForm : Form
	{
		private AssetPreloadData lastSelectedItem;
		private AssetPreloadData lastLoadedAsset;

		private FMOD.System system;
		private Sound sound;
		private Channel channel;
		private SoundGroup masterSoundGroup;
		private MODE loopMode = MODE.LOOP_OFF;
		private uint FMODlenms;
		private float FMODVolume = 0.8f;

		private Bitmap imageTexture;

		#region GLControl

		private bool glControlLoaded;
		private int mdx, mdy;
		private bool lmdown, rmdown;
		private int pgmID, pgmColorID, pgmBlackID;
		private int attributeVertexPosition;
		private int attributeNormalDirection;
		private int attributeVertexColor;
		private int uniformModelMatrix;
		private int uniformViewMatrix;
		private int uniformProjMatrix;
		private int vao;
		private Vector3[] vertexData;
		private Vector3[] normalData;
		private Vector3[] normal2Data;
		private Vector4[] colorData;
		private Matrix4 modelMatrixData;
		private Matrix4 viewMatrixData;
		private Matrix4 projMatrixData;
		private int[] indiceData;
		private int wireFrameMode;
		private int shadeMode;
		private int normalMode;

		#endregion

		//asset list sorting helpers
		private int firstSortColumn = -1;
		private int secondSortColumn;
		private bool reverseSort;
		private bool enableFiltering;

		//tree search
		private int nextGObject;
		private List<GameObjectTreeNode> treeSrcResults = new List<GameObjectTreeNode>();

		[DllImport("gdi32.dll")]
		private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, [In] ref uint pcFonts);

		public AssetStudioForm()
		{
			Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

			InitializeComponent();

			displayOriginalName.Checked = (bool) Settings.Default["displayOriginalName"];
			displayAll.Checked = (bool) Settings.Default["displayAll"];
			displayInfo.Checked = (bool) Settings.Default["displayInfo"];
			enableLiveSearch.Checked = (bool) Settings.Default["enableLiveSearch"];
			enablePreview.Checked = (bool) Settings.Default["enablePreview"];
			openAfterExport.Checked = (bool) Settings.Default["openAfterExport"];
			assetGroupOptions.SelectedIndex = (int) Settings.Default["assetGroupOption"];

			FMODinit();

			// UI
			Studio.SetProgressBarValue = SetProgressBarValue;
			Studio.SetProgressBarMaximum = SetProgressBarMaximum;
			Studio.ProgressBarPerformStep = ProgressBarPerformStep;
			Studio.StatusStripUpdate = StatusStripUpdate;
			Studio.ProgressBarMaximumAdd = ProgressBarMaximumAdd;
		}

		private void loadFile_Click(object sender, EventArgs e)
		{
			if (openFileDialog1.ShowDialog() == DialogResult.OK)
			{
				resetForm();
				ThreadPool.QueueUserWorkItem(state =>
				                             {
					                             mainPath = Path.GetDirectoryName(openFileDialog1.FileNames[0]);
					                             MergeSplitAssets(mainPath);

					                             string[] readFile = ProcessingSplitFiles(openFileDialog1.FileNames.ToList());
					                             foreach (string i in readFile)
					                             {
						                             importFiles.Add(i);
						                             importFilesHash.Add(Path.GetFileName(i)?.ToUpper());
					                             }

					                             SetProgressBarValue(0);
					                             SetProgressBarMaximum(importFiles.Count);

					                             //use a for loop because list size can change
					                             for (var f = 0; f < importFiles.Count; f++)
					                             {
						                             LoadFile(importFiles[f]);
						                             ProgressBarPerformStep();
					                             }

					                             importFilesHash.Clear();
					                             assetsfileListHash.Clear();

					                             BuildAssetStrucutres();
				                             });
			}
		}

		private void loadFolder_Click(object sender, EventArgs e)
		{
			var openFolderDialog1 = new OpenFolderDialog();
			if (openFolderDialog1.ShowDialog(this) == DialogResult.OK)
			{
				resetForm();
				ThreadPool.QueueUserWorkItem(state =>
				                             {
					                             mainPath = openFolderDialog1.Folder;
					                             MergeSplitAssets(mainPath);
					                             List<string> files = Directory.GetFiles(mainPath, "*.*", SearchOption.AllDirectories).ToList();
					                             string[] readFile = ProcessingSplitFiles(files);
					                             foreach (string i in readFile)
					                             {
						                             importFiles.Add(i);
						                             importFilesHash.Add(Path.GetFileName(i));
					                             }
					                             SetProgressBarValue(0);
					                             SetProgressBarMaximum(importFiles.Count);
					                             //use a for loop because list size can change
					                             for (var f = 0; f < importFiles.Count; f++)
					                             {
						                             LoadFile(importFiles[f]);
						                             ProgressBarPerformStep();
					                             }
					                             importFilesHash.Clear();
					                             assetsfileListHash.Clear();
					                             BuildAssetStrucutres();
				                             });
			}
		}

		private void extractFileToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var openBundleDialog = new OpenFileDialog
			{
				Filter = "All types|*.*",
				FilterIndex = 1,
				RestoreDirectory = true,
				Multiselect = true
			};

			if (openBundleDialog.ShowDialog() == DialogResult.OK)
			{
				progressBar1.Value = 0;
				progressBar1.Maximum = openBundleDialog.FileNames.Length;
				ExtractFile(openBundleDialog.FileNames);
			}

			openBundleDialog.Dispose();
		}

		private void extractFolderToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var openFolderDialog1 = new OpenFolderDialog();
			if (openFolderDialog1.ShowDialog(this) == DialogResult.OK)
			{
				string[] files = Directory.GetFiles(openFolderDialog1.Folder, "*.*", SearchOption.AllDirectories);
				progressBar1.Value = 0;
				progressBar1.Maximum = files.Length;
				ExtractFile(files);
			}
		}

		private void BuildAssetStrucutres()
		{
			if (assetsfileList.Count == 0)
			{
				StatusStripUpdate("No file was loaded.");
				return;
			}

			BuildAssetStructures(!dontLoadAssetsMenuItem.Checked, displayAll.Checked, !dontBuildHierarchyMenuItem.Checked, buildClassStructuresMenuItem.Checked, displayOriginalName.Checked);

			BeginInvoke(new Action(() =>
			                       {
				                       if (!string.IsNullOrEmpty(productName))
				                       {
					                       Text = $"AssetStudio - {productName} - {assetsfileList[0].unityVersion} - {assetsfileList[0].platformStr}";
				                       }
				                       else if (assetsfileList.Count > 0)
				                       {
					                       Text = $"AssetStudio - no productName - {assetsfileList[0].unityVersion} - {assetsfileList[0].platformStr}";
				                       }
				                       if (!dontLoadAssetsMenuItem.Checked)
				                       {
					                       assetListView.VirtualListSize = visibleAssets.Count;
					                       //will only work if ListView is visible
					                       resizeAssetListColumns();
				                       }
				                       if (!dontBuildHierarchyMenuItem.Checked)
				                       {
					                       if (this.sceneTreeView != null)
					                       {
						                       sceneTreeView.BeginUpdate();
						                       if (treeNodeCollection != null)
						                       {
							                       GameObjectTreeNode[] treeNodeArray = treeNodeCollection.ToArray<GameObjectTreeNode>();

							                       sceneTreeView.Nodes.AddRange(treeNodeArray.ToArray<TreeNode>());

							                       foreach (TreeNode node in sceneTreeView.Nodes)
							                       {
								                       node.HideCheckBox();
							                       }
						                       }
						                       sceneTreeView.EndUpdate();
					                       }
				                       }
				                       if (buildClassStructuresMenuItem.Checked)
				                       {
					                       if (this.classesListView != null)
					                       {
						                       classesListView.BeginUpdate();
						                       foreach (KeyValuePair<string, SortedDictionary<int, TypeTreeItem>> version in AllTypeMap)
						                       {
							                       var versionGroup = new ListViewGroup(version.Key);
							                       classesListView.Groups.Add(versionGroup);

							                       foreach (KeyValuePair<int, TypeTreeItem> uclass in version.Value)
							                       {
								                       uclass.Value.Group = versionGroup;
								                       classesListView.Items.Add(uclass.Value);
							                       }
						                       }
						                       classesListView.EndUpdate();
					                       }
				                       }

				                       ClassIDType[] types = exportableAssets.Select(x => x.Type).Distinct().OrderBy(x => x.ToString()).ToArray();

				                       // ReSharper disable once LoopCanBePartlyConvertedToQuery
				                       foreach (ClassIDType type in types)
				                       {
					                       var typeItem = new ToolStripMenuItem
					                       {
						                       CheckOnClick = true,
						                       Name = type.ToString(),
						                       Size = new Size(180, 22),
						                       Text = type.ToString()
					                       };
					                       typeItem.Click += typeToolStripMenuItem_Click;
					                       filterTypeToolStripMenuItem.DropDownItems.Add(typeItem);
				                       }
				                       allToolStripMenuItem.Checked = true;
				                       StatusStripUpdate($"Finished loading {assetsfileList.Count} files with {assetListView.Items.Count} exportable assets.");
				                       treeSearch.Select();
			                       }));
		}

		private void typeToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var typeItem = (ToolStripMenuItem) sender;
			if (typeItem != allToolStripMenuItem)
			{
				allToolStripMenuItem.Checked = false;
			}
			else if (allToolStripMenuItem.Checked)
			{
				for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
				{
					var item = (ToolStripMenuItem) filterTypeToolStripMenuItem.DropDownItems[i];
					item.Checked = false;
				}
			}
			FilterAssetList();
		}

		private void AssetStudioForm_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control && e.Alt && e.KeyCode == Keys.D)
			{
				debugMenuItem.Visible = !debugMenuItem.Visible;
				buildClassStructuresMenuItem.Checked = debugMenuItem.Visible;
				dontLoadAssetsMenuItem.Checked = debugMenuItem.Visible;
				dontBuildHierarchyMenuItem.Checked = debugMenuItem.Visible;
				if (tabControl1.TabPages.Contains(tabPage3))
				{
					tabControl1.TabPages.Remove(tabPage3);
				}
				else
				{
					tabControl1.TabPages.Add(tabPage3);
				}
			}

			if (!this.glControl1.Visible)
			{
				return;
			}

			if (!e.Control)
			{
				return;
			}

			switch (e.KeyCode)
			{
				case Keys.W:
					if (e.Control) //Toggle WireFrame
					{
						this.wireFrameMode = (this.wireFrameMode + 1) % 3;
						this.glControl1.Invalidate();
					}
					break;
				case Keys.S:
					if (e.Control) //Toggle Shade
					{
						this.shadeMode = (this.shadeMode + 1) % 2;
						this.glControl1.Invalidate();
					}
					break;
				case Keys.N:
					if (e.Control) //Normal mode
					{
						this.normalMode = (this.normalMode + 1) % 2;
						this.createVAO();
						this.glControl1.Invalidate();
					}
					break;
			}
		}

		private void dontLoadAssetsMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			if (dontLoadAssetsMenuItem.Checked)
			{
				dontBuildHierarchyMenuItem.Checked = true;
				dontBuildHierarchyMenuItem.Enabled = false;
			}
			else
			{
				dontBuildHierarchyMenuItem.Enabled = true;
			}
		}

		private void exportClassStructuresMenuItem_Click(object sender, EventArgs e)
		{
			if (AllTypeMap.Count <= 0)
			{
				return;
			}

			var saveFolderDialog1 = new OpenFolderDialog();

			if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			this.progressBar1.Value = 0;
			this.progressBar1.Maximum = AllTypeMap.Count;

			string savePath = saveFolderDialog1.Folder;

			foreach (KeyValuePair<string, SortedDictionary<int, TypeTreeItem>> version in AllTypeMap)
			{
				if (version.Value.Count > 0)
				{
					string versionPath = savePath + "\\" + version.Key;
					Directory.CreateDirectory(versionPath);

					foreach (KeyValuePair<int, TypeTreeItem> uclass in version.Value)
					{
						string saveFile = $"{versionPath}\\{uclass.Key} {uclass.Value.Text}.txt";
						using (var TXTwriter = new StreamWriter(saveFile))
						{
							TXTwriter.Write(uclass.Value.ToString());
						}
					}
				}

				this.progressBar1.PerformStep();
			}

			this.StatusStripUpdate("Finished exporting class structures");
			this.progressBar1.Value = 0;
		}

		private void enablePreview_Check(object sender, EventArgs e)
		{
			if (lastLoadedAsset != null)
			{
				switch (lastLoadedAsset.Type)
				{
					case ClassIDType.Texture2D:
					case ClassIDType.Sprite:
						if (enablePreview.Checked && imageTexture != null)
						{
							previewPanel.BackgroundImage = imageTexture;
						}
						else
						{
							previewPanel.BackgroundImage = Resources.preview;
							previewPanel.BackgroundImageLayout = ImageLayout.Center;
						}
						break;
					case ClassIDType.Shader:
					case ClassIDType.TextAsset:
					case ClassIDType.MonoBehaviour:
						textPreviewBox.Visible = !textPreviewBox.Visible;
						break;
					case ClassIDType.Font:
						fontPreviewBox.Visible = !fontPreviewBox.Visible;
						break;
					case ClassIDType.AudioClip:
						FMODpanel.Visible = !FMODpanel.Visible;

						if (sound != null && channel != null)
						{
							RESULT result = channel.isPlaying(out bool playing);
							if (result == RESULT.OK && playing)
							{
								result = channel.stop();
								FMODreset();
							}
						}
						else if (FMODpanel.Visible)
						{
							PreviewAsset(lastLoadedAsset);
						}
						break;
				}
			}
			else if (lastSelectedItem != null && enablePreview.Checked)
			{
				lastLoadedAsset = lastSelectedItem;
				PreviewAsset(lastLoadedAsset);
			}

			Settings.Default["enablePreview"] = enablePreview.Checked;
			Settings.Default.Save();
		}

		private void displayAssetInfo_Check(object sender, EventArgs e)
		{
			if (displayInfo.Checked && assetInfoLabel.Text != null)
			{
				assetInfoLabel.Visible = true;
			}
			else
			{
				assetInfoLabel.Visible = false;
			}

			Settings.Default["displayInfo"] = displayInfo.Checked;
			Settings.Default.Save();
		}

		private void MenuItem_CheckedChanged(object sender, EventArgs e)
		{
			Settings.Default[((ToolStripMenuItem) sender).Name] = ((ToolStripMenuItem) sender).Checked;
			Settings.Default.Save();
		}

		private void assetGroupOptions_SelectedIndexChanged(object sender, EventArgs e)
		{
			Settings.Default["assetGroupOption"] = ((ToolStripComboBox) sender).SelectedIndex;
			Settings.Default.Save();
		}

		private void showExpOpt_Click(object sender, EventArgs e)
		{
			using (var exportOpt = new ExportOptions())
			{
				exportOpt.ShowDialog();
			}
		}

		private void assetListView_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
		{
			e.Item = visibleAssets[e.ItemIndex];
		}

		private void tabPageSelected(object sender, TabControlEventArgs e)
		{
			switch (e.TabPageIndex)
			{
				case 0:
					treeSearch.Select();
					break;
				case 1:
					resizeAssetListColumns(); //required because the ListView is not visible on app launch
					classPreviewPanel.Visible = false;
					previewPanel.Visible = true;
					listSearch.Select();
					break;
				case 2:
					previewPanel.Visible = false;
					classPreviewPanel.Visible = true;
					break;
			}
		}

		private void treeSearch_Enter(object sender, EventArgs e)
		{
			if (this.treeSearch.Text != " Search ")
			{
				return;
			}

			this.treeSearch.Text = "";
			this.treeSearch.ForeColor = SystemColors.WindowText;
		}

		private void treeSearch_Leave(object sender, EventArgs e)
		{
			if (this.treeSearch.Text != "")
			{
				return;
			}

			this.treeSearch.Text = " Search ";
			this.treeSearch.ForeColor = SystemColors.GrayText;
		}

		private void treeSearch_TextChanged(object sender, EventArgs e)
		{
			treeSrcResults.Clear();
			nextGObject = 0;
		}

		private void treeSearch_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode != Keys.Enter)
			{
				return;
			}

			if (this.treeSrcResults.Count == 0)
			{
				foreach (GameObjectTreeNode node in treeNodeDictionary.Values)
				{
					if (node.Text.IndexOf(this.treeSearch.Text, StringComparison.CurrentCultureIgnoreCase) >= 0)
					{
						this.treeSrcResults.Add(node);
					}
				}
			}

			if (this.treeSrcResults.Count > 0)
			{
				if (this.nextGObject >= this.treeSrcResults.Count)
				{
					this.nextGObject = 0;
				}

				this.treeSrcResults[this.nextGObject].EnsureVisible();
				this.sceneTreeView.SelectedNode = this.treeSrcResults[this.nextGObject];
				this.nextGObject++;
			}
		}

		private void sceneTreeView_AfterCheck(object sender, TreeViewEventArgs e)
		{
			foreach (TreeNode childNode in e.Node.Nodes)
			{
				childNode.Checked = e.Node.Checked;
			}
		}

		private void resizeAssetListColumns()
		{
			// TODO: defer to user preferences for column widths
			assetListView.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
		}

		private void tabPage2_Resize(object sender, EventArgs e)
		{
			resizeAssetListColumns();
		}

		private void listSearch_Enter(object sender, EventArgs e)
		{
			if (this.listSearch.Text != " Filter ")
			{
				return;
			}

			this.listSearch.Text = "";
			this.listSearch.ForeColor = SystemColors.WindowText;
			this.enableFiltering = true;
		}

		private void listSearch_Leave(object sender, EventArgs e)
		{
			if (this.listSearch.Text != "")
			{
				return;
			}

			this.enableFiltering = false;
			this.listSearch.Text = " Filter ";
			this.listSearch.ForeColor = SystemColors.GrayText;
		}

		private void ListSearchTextChanged(object sender, EventArgs e)
		{
			if (enableFiltering && enableLiveSearch.Checked)
			{
				FilterAssetList();
			}
		}

		[SuppressMessage("ReSharper", "StringCompareToIsCultureSpecific")]
		private void assetListView_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			if (firstSortColumn != e.Column)
			{
				//sorting column has been changed
				reverseSort = false;
				secondSortColumn = firstSortColumn;
			}
			else
			{
				reverseSort = !reverseSort;
			}

			firstSortColumn = e.Column;

			assetListView.BeginUpdate();
			assetListView.SelectedIndices.Clear();

			switch (e.Column)
			{
				case 0:
					visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
					                   {
						                   int xdiff = reverseSort ? b.Text.CompareTo(a.Text) : a.Text.CompareTo(b.Text);
						                   if (xdiff != 0)
						                   {
							                   return xdiff;
						                   }
						                   return secondSortColumn == 1 ? a.TypeString.CompareTo(b.TypeString) : a.FullSize.CompareTo(b.FullSize);
					                   });
					break;
				case 1:
					visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
					                   {
						                   int xdiff = reverseSort ? b.TypeString.CompareTo(a.TypeString) : a.TypeString.CompareTo(b.TypeString);
						                   if (xdiff != 0)
						                   {
							                   return xdiff;
						                   }
						                   return secondSortColumn == 2 ? a.FullSize.CompareTo(b.FullSize) : a.Text.CompareTo(b.Text);
					                   });
					break;
				case 2:
					visibleAssets.Sort(delegate(AssetPreloadData a, AssetPreloadData b)
					                   {
						                   int xdiff = reverseSort ? b.FullSize.CompareTo(a.FullSize) : a.FullSize.CompareTo(b.FullSize);
						                   if (xdiff != 0)
						                   {
							                   return xdiff;
						                   }
						                   return secondSortColumn == 1 ? a.TypeString.CompareTo(b.TypeString) : a.Text.CompareTo(b.Text);
					                   });
					break;
			}

			assetListView.EndUpdate();

			resizeAssetListColumns();
		}

		private void selectAsset(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			previewPanel.BackgroundImage = Resources.preview;
			previewPanel.BackgroundImageLayout = ImageLayout.Center;
			assetInfoLabel.Visible = false;
			assetInfoLabel.Text = null;
			textPreviewBox.Visible = false;
			fontPreviewBox.Visible = false;
			FMODpanel.Visible = false;
			glControl1.Visible = false;
			lastLoadedAsset = null;
			StatusStripUpdate("");

			FMODreset();

			lastSelectedItem = (AssetPreloadData) e.Item;

			if (!e.IsSelected)
			{
				return;
			}

			if (this.enablePreview.Checked)
			{
				this.lastLoadedAsset = this.lastSelectedItem;
				this.PreviewAsset(this.lastLoadedAsset);
			}

			if (this.displayInfo.Checked && this.assetInfoLabel.Text != null) //only display the label if asset has info text
			{
				this.assetInfoLabel.Text = this.lastSelectedItem.InfoText;
				this.assetInfoLabel.Visible = true;
			}
		}

		private void classesListView_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
		{
			if (e.IsSelected)
			{
				classTextBox.Text = ((TypeTreeItem) classesListView.SelectedItems[0]).ToString();
			}
		}

		private void PreviewAsset(AssetPreloadData asset)
		{
			switch (asset.Type)
			{
				case ClassIDType.Texture2D:
				{
					this.PreviewAsset_Texture2D(asset);
					break;
				}
				case ClassIDType.AudioClip:
				{
					this.PreviewAsset_AudioClip(asset);
					break;
				}
				case ClassIDType.Shader:
				{
					this.PreviewAsset_Shader(asset);
					break;
				}
				case ClassIDType.TextAsset:
				{
					this.PreviewAsset_TextAsset(asset);
					break;
				}
				case ClassIDType.MonoScript:
					AddNodes(this.monoPreviewBox, asset, false);
					break;
				case ClassIDType.MonoBehaviour:
					AddNodes(this.monoPreviewBox, asset, true);
					break;
				case ClassIDType.Font:
				{
					this.PreviewAsset_Font(asset);
					break;
				}
				case ClassIDType.Mesh:
					if (this.PreviewAsset_Mesh(asset))
					{
						return;
					}
					break;
				case ClassIDType.VideoClip:
				case ClassIDType.MovieTexture:
					StatusStripUpdate("Only supported export.");
					break;
				case ClassIDType.Sprite:
					this.PreviewAsset_Sprite(asset);
					break;
				case ClassIDType.Animator:
					StatusStripUpdate("Can be exported to FBX file.");
					break;
				case ClassIDType.AnimationClip:
					StatusStripUpdate("Can be exported with Animator or objects");
					break;
				default:
					this.PreviewAsset_Default(asset);
					break;
			}

			switch (asset.Type)
			{
				case ClassIDType.MonoBehaviour:
				case ClassIDType.MonoScript:
					this.monoPreviewBox.Visible = true;
					this.textPreviewBox.Visible = false;
					break;
				default:
					this.monoPreviewBox.Nodes.Clear();
					this.monoPreviewBox.Visible = false;
					break;
			}
		}

		private void PreviewAsset_Default(AssetPreloadData asset)
		{
			string str = asset.Dump();

			if (str != null)
			{
				this.textPreviewBox.Text = str;
				this.textPreviewBox.Visible = true;
			}
			else
			{
				this.StatusStripUpdate("Only supported export the raw file.");
			}
		}

		private void PreviewAsset_AudioClip(AssetPreloadData asset)
		{
			var m_AudioClip = new AudioClip(asset, true);

			//Info
			asset.InfoText = "Compression format: ";
			if (m_AudioClip.version[0] < 5)
			{
				switch (m_AudioClip.m_Type)
				{
					case AudioType.ACC:
						asset.InfoText += "Acc";
						break;
					case AudioType.AIFF:
						asset.InfoText += "AIFF";
						break;
					case AudioType.IT:
						asset.InfoText += "Impulse tracker";
						break;
					case AudioType.MOD:
						asset.InfoText += "Protracker / Fasttracker MOD";
						break;
					case AudioType.MPEG:
						asset.InfoText += "MP2/MP3 MPEG";
						break;
					case AudioType.OGGVORBIS:
						asset.InfoText += "Ogg vorbis";
						break;
					case AudioType.S3M:
						asset.InfoText += "ScreamTracker 3";
						break;
					case AudioType.WAV:
						asset.InfoText += "Microsoft WAV";
						break;
					case AudioType.XM:
						asset.InfoText += "FastTracker 2 XM";
						break;
					case AudioType.XMA:
						asset.InfoText += "Xbox360 XMA";
						break;
					case AudioType.VAG:
						asset.InfoText += "PlayStation Portable ADPCM";
						break;
					case AudioType.AUDIOQUEUE:
						asset.InfoText += "iPhone";
						break;
					default:
						asset.InfoText += "Unknown";
						break;
				}
			}
			else
			{
				switch (m_AudioClip.m_CompressionFormat)
				{
					case AudioCompressionFormat.PCM:
						asset.InfoText += "PCM";
						break;
					case AudioCompressionFormat.Vorbis:
						asset.InfoText += "Vorbis";
						break;
					case AudioCompressionFormat.ADPCM:
						asset.InfoText += "ADPCM";
						break;
					case AudioCompressionFormat.MP3:
						asset.InfoText += "MP3";
						break;
					case AudioCompressionFormat.VAG:
						asset.InfoText += "PlayStation Portable ADPCM";
						break;
					case AudioCompressionFormat.HEVAG:
						asset.InfoText += "PSVita ADPCM";
						break;
					case AudioCompressionFormat.XMA:
						asset.InfoText += "Xbox360 XMA";
						break;
					case AudioCompressionFormat.AAC:
						asset.InfoText += "AAC";
						break;
					case AudioCompressionFormat.GCADPCM:
						asset.InfoText += "Nintendo 3DS/Wii DSP";
						break;
					case AudioCompressionFormat.ATRAC9:
						asset.InfoText += "PSVita ATRAC9";
						break;
					default:
						asset.InfoText += "Unknown";
						break;
				}
			}

			if (m_AudioClip.m_AudioData == null)
			{
				return;
			}

			var exinfo = new CREATESOUNDEXINFO();

			exinfo.cbsize = Marshal.SizeOf(exinfo);
			exinfo.length = (uint) m_AudioClip.m_Size;

			RESULT result = this.system.createSound(m_AudioClip.m_AudioData, MODE.OPENMEMORY | this.loopMode, ref exinfo, out this.sound);
			if (this.ERRCHECK(result))
			{
				return;
			}

			result = this.sound.getSubSound(0, out Sound subsound);
			if (result == RESULT.OK)
			{
				this.sound = subsound;
			}

			result = this.sound.getLength(out this.FMODlenms, TIMEUNIT.MS);
			if (this.ERRCHECK(result))
			{
				return;
			}

			result = this.system.playSound(this.sound, null, true, out this.channel);
			if (this.ERRCHECK(result))
			{
				return;
			}

			this.FMODpanel.Visible = true;

			result = this.channel.getFrequency(out float frequency);
			if (this.ERRCHECK(result))
			{
				return;
			}

			this.FMODinfoLabel.Text = frequency + " Hz";
			this.FMODtimerLabel.Text = $"0:0.0 / {this.FMODlenms / 1000 / 60}:{this.FMODlenms / 1000 % 60}.{this.FMODlenms / 10 % 100}";
		}

		private void PreviewAsset_Font(AssetPreloadData asset)
		{
			var m_Font = new Font(asset);

			if (m_Font.m_FontData != null)
			{
				IntPtr data = Marshal.AllocCoTaskMem(m_Font.m_FontData.Length);
				Marshal.Copy(m_Font.m_FontData, 0, data, m_Font.m_FontData.Length);

				// We HAVE to do this to register the font to the system (Weird .NET bug !)
				uint cFonts = 0;

				IntPtr re = AddFontMemResourceEx(data, (uint) m_Font.m_FontData.Length, IntPtr.Zero, ref cFonts);

				if (re != IntPtr.Zero)
				{
					using (var pfc = new PrivateFontCollection())
					{
						pfc.AddMemoryFont(data, m_Font.m_FontData.Length);
						Marshal.FreeCoTaskMem(data);

						if (pfc.Families.Length <= 0)
						{
							return;
						}

						this.fontPreviewBox.SelectionStart = 0;
						this.fontPreviewBox.SelectionLength = 80;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 16, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 81;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 12, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 138;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 18, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 195;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 24, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 252;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 36, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 309;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 48, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 366;
						this.fontPreviewBox.SelectionLength = 56;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 60, FontStyle.Regular);
						this.fontPreviewBox.SelectionStart = 423;
						this.fontPreviewBox.SelectionLength = 55;
						this.fontPreviewBox.SelectionFont = new System.Drawing.Font(pfc.Families[0], 72, FontStyle.Regular);
						this.fontPreviewBox.Visible = true;
					}

					return;
				}
			}

			this.StatusStripUpdate("Unsupported font for preview. Try to export.");
		}

		private bool PreviewAsset_Mesh(AssetPreloadData asset)
		{
			var m_Mesh = new Mesh(asset);

			if (m_Mesh.m_VertexCount > 0)
			{
				this.viewMatrixData = Matrix4.CreateRotationY(-(float) Math.PI / 4) * Matrix4.CreateRotationX(-(float) Math.PI / 6);

				if (this.CalculateMeshVertices(m_Mesh, out int count))
				{
					return true;
				}

				float[] min = this.CalculateMeshBoundingMin(m_Mesh, count, out float[] max);

				this.CalculateMeshMatrix(max, min);

				this.CalculateMeshIndices(m_Mesh);

				this.CalculateMeshNormals(m_Mesh, count);

				this.CalculateMeshColors(m_Mesh);

				this.glControl1.Visible = true;
				this.createVAO();
			}

			this.StatusStripUpdate("Using OpenGL Version: " + GL.GetString(StringName.Version) + "\n" + "'Mouse Left'=Rotate | 'Mouse Right'=Move | 'Mouse Wheel'=Zoom \n" + "'Ctrl W'=Wireframe | 'Ctrl S'=Shade | 'Ctrl N'=ReNormal ");
			return false;
		}

		private bool CalculateMeshVertices(Mesh m_Mesh, out int count)
		{
			if (m_Mesh.m_Vertices == null || m_Mesh.m_Vertices.Length == 0)
			{
				this.StatusStripUpdate("Mesh can't be previewed.");
				count = 0;
				return true;
			}

			count = 3;
			if (m_Mesh.m_Vertices.Length == m_Mesh.m_VertexCount * 4)
			{
				count = 4;
			}

			this.vertexData = new Vector3[m_Mesh.m_VertexCount];
			return false;
		}

		private float[] CalculateMeshBoundingMin(Mesh m_Mesh, int count, out float[] max)
		{
			var min = new float[3];
			max = new float[3];

			for (var i = 0; i < 3; i++)
			{
				min[i] = m_Mesh.m_Vertices[i];
				max[i] = m_Mesh.m_Vertices[i];
			}

			for (var v = 0; v < m_Mesh.m_VertexCount; v++)
			{
				for (var i = 0; i < 3; i++)
				{
					min[i] = Math.Min(min[i], m_Mesh.m_Vertices[v * count + i]);
					max[i] = Math.Max(max[i], m_Mesh.m_Vertices[v * count + i]);
				}

				this.vertexData[v] = new Vector3(m_Mesh.m_Vertices[v * count], m_Mesh.m_Vertices[v * count + 1], m_Mesh.m_Vertices[v * count + 2]);
			}

			return min;
		}

		private void CalculateMeshMatrix(float[] max, float[] min)
		{
			Vector3 dist = Vector3.One, offset = Vector3.Zero;

			for (var i = 0; i < 3; i++)
			{
				dist[i] = max[i] - min[i];
				offset[i] = (max[i] + min[i]) / 2;
			}

			float d = Math.Max(1e-5f, dist.Length);
			this.modelMatrixData = Matrix4.CreateTranslation(-offset) * Matrix4.CreateScale(2f / d);
		}

		private void CalculateMeshIndices(Mesh m_Mesh)
		{
			this.indiceData = new int[m_Mesh.m_Indices.Count];

			for (var i = 0; i < m_Mesh.m_Indices.Count; i = i + 3)
			{
				this.indiceData[i] = (int) m_Mesh.m_Indices[i];
				this.indiceData[i + 1] = (int) m_Mesh.m_Indices[i + 1];
				this.indiceData[i + 2] = (int) m_Mesh.m_Indices[i + 2];
			}
		}

		private void CalculateMeshNormals(Mesh m_Mesh, int count)
		{
			if (m_Mesh.m_Normals != null && m_Mesh.m_Normals.Length > 0)
			{
				if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 3)
				{
					count = 3;
				}
				else if (m_Mesh.m_Normals.Length == m_Mesh.m_VertexCount * 4)
				{
					count = 4;
				}

				this.normalData = new Vector3[m_Mesh.m_VertexCount];

				for (var n = 0; n < m_Mesh.m_VertexCount; n++)
				{
					this.normalData[n] = new Vector3(m_Mesh.m_Normals[n * count], m_Mesh.m_Normals[n * count + 1], m_Mesh.m_Normals[n * count + 2]);
				}
			}
			else
			{
				this.normalData = null;
			}

			// calculate normal by ourself
			this.normal2Data = new Vector3[m_Mesh.m_VertexCount];
			var normalCalculatedCount = new int[m_Mesh.m_VertexCount];

			for (var i = 0; i < m_Mesh.m_VertexCount; i++)
			{
				this.normal2Data[i] = Vector3.Zero;
				normalCalculatedCount[i] = 0;
			}

			for (var i = 0; i < m_Mesh.m_Indices.Count; i = i + 3)
			{
				Vector3 dir1 = this.vertexData[this.indiceData[i + 1]] - this.vertexData[this.indiceData[i]];
				Vector3 dir2 = this.vertexData[this.indiceData[i + 2]] - this.vertexData[this.indiceData[i]];
				Vector3 normal = Vector3.Cross(dir1, dir2);
				normal.Normalize();

				for (var j = 0; j < 3; j++)
				{
					this.normal2Data[this.indiceData[i + j]] += normal;
					normalCalculatedCount[this.indiceData[i + j]]++;
				}
			}

			for (var i = 0; i < m_Mesh.m_VertexCount; i++)
			{
				if (normalCalculatedCount[i] == 0)
				{
					this.normal2Data[i] = new Vector3(0, 1, 0);
				}
				else
				{
					this.normal2Data[i] /= normalCalculatedCount[i];
				}
			}
		}

		private void CalculateMeshColors(Mesh m_Mesh)
		{
			if (m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 3)
			{
				this.colorData = new Vector4[m_Mesh.m_VertexCount];

				for (var c = 0; c < m_Mesh.m_VertexCount; c++)
				{
					this.colorData[c] = new Vector4(m_Mesh.m_Colors[c * 3], m_Mesh.m_Colors[c * 3 + 1], m_Mesh.m_Colors[c * 3 + 2], 1.0f);
				}
			}
			else if (m_Mesh.m_Colors != null && m_Mesh.m_Colors.Length == m_Mesh.m_VertexCount * 4)
			{
				this.colorData = new Vector4[m_Mesh.m_VertexCount];

				for (var c = 0; c < m_Mesh.m_VertexCount; c++)
				{
					this.colorData[c] = new Vector4(m_Mesh.m_Colors[c * 4], m_Mesh.m_Colors[c * 4 + 1], m_Mesh.m_Colors[c * 4 + 2], m_Mesh.m_Colors[c * 4 + 3]);
				}
			}
			else
			{
				this.colorData = new Vector4[m_Mesh.m_VertexCount];

				for (var c = 0; c < m_Mesh.m_VertexCount; c++)
				{
					this.colorData[c] = new Vector4(0.5f, 0.5f, 0.5f, 1.0f);
				}
			}
		}

		private void PreviewAsset_Shader(AssetPreloadData asset)
		{
			var m_TextAsset = new Shader(asset);

			string m_Script_Text = Encoding.UTF8.GetString(m_TextAsset.m_Script);

			m_Script_Text = Regex.Replace(m_Script_Text, "(?<!\r)\n", "\r\n");
			m_Script_Text = m_Script_Text.Replace("\0", "\\0");

			this.textPreviewBox.Text = m_Script_Text;
			this.textPreviewBox.Visible = true;
		}

		private void PreviewAsset_Sprite(AssetPreloadData asset)
		{
			this.imageTexture?.Dispose();
			this.imageTexture = SpriteHelper.GetImageFromSprite(new Sprite(asset));

			if (this.imageTexture != null)
			{
				asset.InfoText = $"Width: {this.imageTexture.Width}\nHeight: {this.imageTexture.Height}\n";

				this.previewPanel.BackgroundImage = this.imageTexture;

				if (this.imageTexture.Width > this.previewPanel.Width || this.imageTexture.Height > this.previewPanel.Height)
				{
					this.previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
				}
				else
				{
					this.previewPanel.BackgroundImageLayout = ImageLayout.Center;
				}
			}
			else
			{
				this.StatusStripUpdate("Unsupported sprite for preview.");
			}
		}

		private void PreviewAsset_TextAsset(AssetPreloadData asset)
		{
			var m_TextAsset = new TextAsset(asset);

			string m_Script_Text = Encoding.UTF8.GetString(m_TextAsset.m_Script);
			m_Script_Text = Regex.Replace(m_Script_Text, "(?<!\r)\n", "\r\n");

			this.textPreviewBox.Text = m_Script_Text;
			this.textPreviewBox.Visible = true;
		}

		private void PreviewAsset_Texture2D(AssetPreloadData asset)
		{
			this.imageTexture?.Dispose();
			var m_Texture2D = new Texture2D(asset, true);

			//Info
			asset.InfoText = $"Width: {m_Texture2D.m_Width}\nHeight: {m_Texture2D.m_Height}\nFormat: {m_Texture2D.m_TextureFormat}";

			switch (m_Texture2D.m_FilterMode)
			{
				case 0:
					asset.InfoText += "\nFilter Mode: Point ";
					break;
				case 1:
					asset.InfoText += "\nFilter Mode: Bilinear ";
					break;
				case 2:
					asset.InfoText += "\nFilter Mode: Trilinear ";
					break;
			}

			asset.InfoText += $"\nAnisotropic level: {m_Texture2D.m_Aniso}\nMip map bias: {m_Texture2D.m_MipBias}";

			switch (m_Texture2D.m_WrapMode)
			{
				case 0:
					asset.InfoText += "\nWrap mode: Repeat";
					break;
				case 1:
					asset.InfoText += "\nWrap mode: Clamp";
					break;
			}

			var converter = new Texture2DConverter(m_Texture2D);
			this.imageTexture = converter.ConvertToBitmap(true);

			if (this.imageTexture != null)
			{
				this.previewPanel.BackgroundImage = this.imageTexture;
				if (this.imageTexture.Width > this.previewPanel.Width || this.imageTexture.Height > this.previewPanel.Height)
				{
					this.previewPanel.BackgroundImageLayout = ImageLayout.Zoom;
				}
				else
				{
					this.previewPanel.BackgroundImageLayout = ImageLayout.Center;
				}
			}
			else
			{
				this.StatusStripUpdate("Unsupported image for preview");
			}
		}

		private void FMODinit()
		{
			FMODreset();

			RESULT result = Factory.System_Create(out system);
			if (ERRCHECK(result))
			{
				return;
			}

			result = system.getVersion(out uint version);
			ERRCHECK(result);
			if (version < VERSION.number)
			{
				MessageBox.Show($"Error!  You are using an old version of FMOD {version:X}.  This program requires {VERSION.number:X}.");
				Application.Exit();
			}

			result = system.init(1, INITFLAGS.NORMAL, IntPtr.Zero);
			if (ERRCHECK(result))
			{
				return;
			}

			result = system.getMasterSoundGroup(out masterSoundGroup);
			if (ERRCHECK(result))
			{
				return;
			}

			result = masterSoundGroup.setVolume(FMODVolume);
			if (ERRCHECK(result))
			{
				return;
			}
		}

		private void FMODreset()
		{
			timer.Stop();
			FMODprogressBar.Value = 0;
			FMODtimerLabel.Text = "0:00.0 / 0:00.0";
			FMODstatusLabel.Text = "Stopped";
			FMODinfoLabel.Text = "";

			if (this.sound == null || !this.sound.isValid())
			{
				return;
			}

			RESULT result = this.sound.release();
			this.ERRCHECK(result);
			this.sound = null;
		}

		private void FMODplayButton_Click(object sender, EventArgs e)
		{
			if (this.sound == null || this.channel == null)
			{
				return;
			}

			this.timer.Start();
			RESULT result = this.channel.isPlaying(out bool playing);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			if (playing)
			{
				result = this.channel.stop();
				if (this.ERRCHECK(result))
				{
					return;
				}

				result = this.system.playSound(this.sound, null, false, out this.channel);
				if (this.ERRCHECK(result))
				{
					return;
				}

				this.FMODpauseButton.Text = "Pause";
			}
			else
			{
				result = this.system.playSound(this.sound, null, false, out this.channel);
				if (this.ERRCHECK(result))
				{
					return;
				}
				this.FMODstatusLabel.Text = "Playing";

				if (this.FMODprogressBar.Value <= 0)
				{
					return;
				}

				uint newms = this.FMODlenms / 1000 * (uint) this.FMODprogressBar.Value;

				result = this.channel.setPosition(newms, TIMEUNIT.MS);
				if (result == RESULT.OK || result == RESULT.ERR_INVALID_HANDLE)
				{
					return;
				}

				if (this.ERRCHECK(result))
				{
					return;
				}
			}
		}

		private void FMODpauseButton_Click(object sender, EventArgs e)
		{
			if (this.sound == null || this.channel == null)
			{
				return;
			}

			RESULT result = this.channel.isPlaying(out bool playing);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			if (!playing)
			{
				return;
			}

			result = this.channel.getPaused(out bool paused);
			if (this.ERRCHECK(result))
			{
				return;
			}

			result = this.channel.setPaused(!paused);
			if (this.ERRCHECK(result))
			{
				return;
			}

			if (paused)
			{
				this.FMODstatusLabel.Text = "Playing";
				this.FMODpauseButton.Text = "Pause";
				this.timer.Start();
			}
			else
			{
				this.FMODstatusLabel.Text = "Paused";
				this.FMODpauseButton.Text = "Resume";
				this.timer.Stop();
			}
		}

		private void FMODstopButton_Click(object sender, EventArgs e)
		{
			if (this.channel == null)
			{
				return;
			}

			RESULT result = this.channel.isPlaying(out bool playing);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			if (!playing)
			{
				return;
			}

			result = this.channel.stop();
			if (this.ERRCHECK(result))
			{
				return;
			}

			//channel = null;
			//don't FMODreset, it will nullify the sound
			this.timer.Stop();
			this.FMODprogressBar.Value = 0;
			this.FMODtimerLabel.Text = "0:00.0 / 0:00.0";
			this.FMODstatusLabel.Text = "Stopped";
			this.FMODpauseButton.Text = "Pause";
		}

		private void FMODloopButton_CheckedChanged(object sender, EventArgs e)
		{
			RESULT result;

			loopMode = FMODloopButton.Checked ? MODE.LOOP_NORMAL : MODE.LOOP_OFF;

			if (sound != null)
			{
				result = sound.setMode(loopMode);
				if (ERRCHECK(result))
				{
					return;
				}
			}

			if (this.channel == null)
			{
				return;
			}

			result = this.channel.isPlaying(out bool playing);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			result = this.channel.getPaused(out bool paused);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			if (!playing && !paused)
			{
				return;
			}

			result = this.channel.setMode(this.loopMode);
			if (this.ERRCHECK(result))
			{
				return;
			}
		}

		private void FMODvolumeBar_ValueChanged(object sender, EventArgs e)
		{
			FMODVolume = Convert.ToSingle(FMODvolumeBar.Value) / 10;

			RESULT result = masterSoundGroup.setVolume(FMODVolume);
			if (ERRCHECK(result))
			{
				return;
			}
		}

		private void FMODprogressBar_Scroll(object sender, EventArgs e)
		{
			if (this.channel == null)
			{
				return;
			}

			uint newms = this.FMODlenms / 1000 * (uint) this.FMODprogressBar.Value;
			this.FMODtimerLabel.Text = $"{newms / 1000 / 60}:{newms / 1000 % 60}.{newms / 10 % 100}/{this.FMODlenms / 1000 / 60}:{this.FMODlenms / 1000 % 60}.{this.FMODlenms / 10 % 100}";
		}

		private void FMODprogressBar_MouseDown(object sender, MouseEventArgs e)
		{
			timer.Stop();
		}

		private void FMODprogressBar_MouseUp(object sender, MouseEventArgs e)
		{
			if (this.channel == null)
			{
				return;
			}

			uint newms = this.FMODlenms / 1000 * (uint) this.FMODprogressBar.Value;

			RESULT result = this.channel.setPosition(newms, TIMEUNIT.MS);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			result = this.channel.isPlaying(out bool playing);
			if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
			{
				if (this.ERRCHECK(result))
				{
					return;
				}
			}

			if (playing)
			{
				this.timer.Start();
			}
		}

		private void timer_Tick(object sender, EventArgs e)
		{
			uint ms = 0;
			var playing = false;
			var paused = false;

			if (channel != null)
			{
				RESULT result = channel.getPosition(out ms, TIMEUNIT.MS);
				if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
				{
					ERRCHECK(result);
				}

				result = channel.isPlaying(out playing);
				if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
				{
					ERRCHECK(result);
				}

				result = channel.getPaused(out paused);
				if (result != RESULT.OK && result != RESULT.ERR_INVALID_HANDLE)
				{
					ERRCHECK(result);
				}
			}

			FMODtimerLabel.Text = $"{ms / 1000 / 60}:{ms / 1000 % 60}.{ms / 10 % 100} / {FMODlenms / 1000 / 60}:{FMODlenms / 1000 % 60}.{FMODlenms / 10 % 100}";
			FMODprogressBar.Value = (int) (ms * 1000 / FMODlenms);
			FMODstatusLabel.Text = paused ? "Paused " : playing ? "Playing" : "Stopped";

			if (system != null && channel != null)
			{
				system.update();
			}
		}

		private bool ERRCHECK(RESULT result)
		{
			if (result == RESULT.OK)
			{
				return false;
			}

			this.FMODreset();
			this.StatusStripUpdate($"FMOD error! {result} - {Error.String(result)}");
			return true;
		}

		private void ExportAssets_Click(object sender, EventArgs e)
		{
			if (exportableAssets.Count > 0)
			{
				var saveFolderDialog1 = new OpenFolderDialog();

				if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				this.timer.Stop();

				List<AssetPreloadData> toExportAssets = null;

				switch (((ToolStripItem) sender).Name)
				{
					case "exportAllAssetsMenuItem":
						toExportAssets = exportableAssets;
						break;
					case "exportFilteredAssetsMenuItem":
						toExportAssets = visibleAssets;
						break;
					case "exportSelectedAssetsMenuItem":
						toExportAssets = new List<AssetPreloadData>(this.assetListView.SelectedIndices.Count);
						foreach (int i in this.assetListView.SelectedIndices)
						{
							toExportAssets.Add((AssetPreloadData) this.assetListView.Items[i]);
						}
						break;
				}

				ExportAssets(saveFolderDialog1.Folder, toExportAssets, this.assetGroupOptions.SelectedIndex, this.openAfterExport.Checked);
			}
			else
			{
				StatusStripUpdate("No exportable assets loaded");
			}
		}

		private void SetProgressBarValue(int value)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() =>
				                       {
					                       progressBar1.Value = value;
				                       }));
			}
			else
			{
				progressBar1.Value = value;
			}
		}

		private void SetProgressBarMaximum(int value)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() =>
				                       {
					                       progressBar1.Maximum = value;
				                       }));
			}
			else
			{
				progressBar1.Maximum = value;
			}
		}

        private int progressBarStepDelta;
        private Stopwatch progressBarStepTimer;
        private static readonly TimeSpan Fps = TimeSpan.FromMilliseconds(1000/30f); // 30fps target

        private void ProgressBarPerformStep()
		{

            Interlocked.Increment(ref this.progressBarStepDelta);

            // timers aren't "real" - minimal alloc here
            Stopwatch sw = Interlocked.CompareExchange(ref this.progressBarStepTimer, Stopwatch.StartNew(), null);

            if (sw == null || sw.Elapsed <= Fps)
            {
                return;
            }

            // don't double-reset timer
            Interlocked.CompareExchange(ref this.progressBarStepTimer, Stopwatch.StartNew(), sw);

			if (this.InvokeRequired)
			{
                this.BeginInvoke(new Action(() =>
                {
                    this.progressBar1.Value += Interlocked.Exchange(ref this.progressBarStepDelta, 0);
                    //this.progressBar1.Update();
                }));

			}
			else
			{
                this.progressBar1.Value += Interlocked.Exchange(ref this.progressBarStepDelta, 0);
                //this.progressBar1.Update();
			}
		}

		private void StatusStripUpdate(string statusText)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() =>
				                       {
					                       toolStripStatusLabel1.Text = statusText;
				                       }));
			}
			else
			{
				toolStripStatusLabel1.Text = statusText;
			}
		}

		private void ProgressBarMaximumAdd(int value)
		{
			if (InvokeRequired)
			{
				BeginInvoke(new Action(() =>
				                       {
					                       progressBar1.Maximum += value;
				                       }));
			}
			else
			{
				progressBar1.Maximum += value;
			}
		}

		private void initOpenTK()
		{
			changeGLSize(glControl1.Size);
			GL.ClearColor(Color.CadetBlue);

			pgmID = GL.CreateProgram();
			loadShader("vs", ShaderType.VertexShader, pgmID, out int vsID);
			loadShader("fs", ShaderType.FragmentShader, pgmID, out int fsID);
			GL.LinkProgram(pgmID);

			pgmColorID = GL.CreateProgram();
			loadShader("vs", ShaderType.VertexShader, pgmColorID, out vsID);
			loadShader("fsColor", ShaderType.FragmentShader, pgmColorID, out fsID);
			GL.LinkProgram(pgmColorID);

			pgmBlackID = GL.CreateProgram();
			loadShader("vs", ShaderType.VertexShader, pgmBlackID, out vsID);
			loadShader("fsBlack", ShaderType.FragmentShader, pgmBlackID, out fsID);
			GL.LinkProgram(pgmBlackID);

			attributeVertexPosition = GL.GetAttribLocation(pgmID, "vertexPosition");
			attributeNormalDirection = GL.GetAttribLocation(pgmID, "normalDirection");
			attributeVertexColor = GL.GetAttribLocation(pgmColorID, "vertexColor");
			uniformModelMatrix = GL.GetUniformLocation(pgmID, "modelMatrix");
			uniformViewMatrix = GL.GetUniformLocation(pgmID, "viewMatrix");
			uniformProjMatrix = GL.GetUniformLocation(pgmID, "projMatrix");
		}

		private void loadShader(string filename, ShaderType type, int program, out int address)
		{
			address = GL.CreateShader(type);
			var str = (string) Resources.ResourceManager.GetObject(filename);
			GL.ShaderSource(address, str);
			GL.CompileShader(address);
			GL.AttachShader(program, address);
			GL.DeleteShader(address);
		}

		private void createVBO(out int vboAddress, Vector3[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr) (data.Length * Vector3.SizeInBytes), data, BufferUsageHint.StaticDraw);
			GL.VertexAttribPointer(address, 3, VertexAttribPointerType.Float, false, 0, 0);
			GL.EnableVertexAttribArray(address);
		}

		private void createVBO(out int vboAddress, Vector4[] data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.BindBuffer(BufferTarget.ArrayBuffer, vboAddress);
			GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr) (data.Length * Vector4.SizeInBytes), data, BufferUsageHint.StaticDraw);
			GL.VertexAttribPointer(address, 4, VertexAttribPointerType.Float, false, 0, 0);
			GL.EnableVertexAttribArray(address);
		}

		private void createVBO(out int vboAddress, Matrix4 data, int address)
		{
			GL.GenBuffers(1, out vboAddress);
			GL.UniformMatrix4(address, false, ref data);
		}

		private void createEBO(out int address, int[] data)
		{
			GL.GenBuffers(1, out address);
			GL.BindBuffer(BufferTarget.ElementArrayBuffer, address);
			GL.BufferData(BufferTarget.ElementArrayBuffer, (IntPtr) (data.Length * sizeof(int)), data, BufferUsageHint.StaticDraw);
		}

		private void createVAO()
		{
			GL.DeleteVertexArray(vao);
			GL.GenVertexArrays(1, out vao);
			GL.BindVertexArray(vao);

			createVBO(out int vboPositions, vertexData, attributeVertexPosition);

			if (normalMode == 0)
			{
				createVBO(out int vboNormals, normal2Data, attributeNormalDirection);
			}
			else
			{
				if (normalData != null)
				{
					this.createVBO(out int vboNormals, this.normalData, this.attributeNormalDirection);
				}
			}

			createVBO(out int vboColors, colorData, attributeVertexColor);
			createVBO(out int vboModelMatrix, modelMatrixData, uniformModelMatrix);
			createVBO(out int vboViewMatrix, viewMatrixData, uniformViewMatrix);
			createVBO(out int vboProjMatrix, projMatrixData, uniformProjMatrix);
			createEBO(out int eboElements, indiceData);

			GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
			GL.BindVertexArray(0);
		}

		private void changeGLSize(Size size)
		{
			GL.Viewport(0, 0, size.Width, size.Height);

			if (size.Width <= size.Height)
			{
				float k = 1.0f * size.Width / size.Height;
				projMatrixData = Matrix4.CreateScale(1, k, 1);
			}
			else
			{
				float k = 1.0f * size.Height / size.Width;
				projMatrixData = Matrix4.CreateScale(k, 1, 1);
			}
		}

		private void preview_Resize(object sender, EventArgs e)
		{
			if (!this.glControlLoaded || !this.glControl1.Visible)
			{
				return;
			}

			this.changeGLSize(this.glControl1.Size);
			this.glControl1.Invalidate();
		}

		private void glControl1_Load(object sender, EventArgs e)
		{
			initOpenTK();
			glControlLoaded = true;
		}

		private void glControl1_Paint(object sender, PaintEventArgs e)
		{
			glControl1.MakeCurrent();
			GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
			GL.Enable(EnableCap.DepthTest);
			GL.DepthFunc(DepthFunction.Lequal);
			GL.BindVertexArray(vao);

			if (wireFrameMode == 0 || wireFrameMode == 2)
			{
				GL.UseProgram(shadeMode == 0 ? pgmID : pgmColorID);
				GL.UniformMatrix4(uniformModelMatrix, false, ref modelMatrixData);
				GL.UniformMatrix4(uniformViewMatrix, false, ref viewMatrixData);
				GL.UniformMatrix4(uniformProjMatrix, false, ref projMatrixData);
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
				GL.DrawElements(BeginMode.Triangles, indiceData.Length, DrawElementsType.UnsignedInt, 0);
			}

			//Wireframe
			if (wireFrameMode == 1 || wireFrameMode == 2)
			{
				GL.Enable(EnableCap.PolygonOffsetLine);
				GL.PolygonOffset(-1, -1);
				GL.UseProgram(pgmBlackID);
				GL.UniformMatrix4(uniformModelMatrix, false, ref modelMatrixData);
				GL.UniformMatrix4(uniformViewMatrix, false, ref viewMatrixData);
				GL.UniformMatrix4(uniformProjMatrix, false, ref projMatrixData);
				GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
				GL.DrawElements(BeginMode.Triangles, indiceData.Length, DrawElementsType.UnsignedInt, 0);
				GL.Disable(EnableCap.PolygonOffsetLine);
			}

			GL.BindVertexArray(0);
			GL.Flush();
			glControl1.SwapBuffers();
		}

		private void glControl1_MouseWheel(object sender, MouseEventArgs e)
		{
			if (!this.glControl1.Visible)
			{
				return;
			}

			this.viewMatrixData *= Matrix4.CreateScale(1 + e.Delta / 1000f);
			this.glControl1.Invalidate();
		}

		private void glControl1_MouseDown(object sender, MouseEventArgs e)
		{
			mdx = e.X;
			mdy = e.Y;

			if (e.Button == MouseButtons.Left)
			{
				lmdown = true;
			}

			if (e.Button == MouseButtons.Right)
			{
				rmdown = true;
			}
		}

		private void glControl1_MouseMove(object sender, MouseEventArgs e)
		{
			if (!this.lmdown && !this.rmdown)
			{
				return;
			}

			float dx = this.mdx - e.X;
			float dy = this.mdy - e.Y;
			this.mdx = e.X;
			this.mdy = e.Y;

			if (this.lmdown)
			{
				dx *= 0.01f;
				dy *= 0.01f;
				this.viewMatrixData *= Matrix4.CreateRotationX(dy);
				this.viewMatrixData *= Matrix4.CreateRotationY(dx);
			}

			if (this.rmdown)
			{
				dx *= 0.003f;
				dy *= 0.003f;
				this.viewMatrixData *= Matrix4.CreateTranslation(-dx, dy, 0);
			}

			this.glControl1.Invalidate();
		}

		private void glControl1_MouseUp(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				lmdown = false;
			}

			if (e.Button == MouseButtons.Right)
			{
				rmdown = false;
			}
		}

		private void resetForm()
		{
			Text = "AssetStudio";

			importFiles.Clear();

			foreach (AssetsFile assetsFile in assetsfileList)
			{
				assetsFile.reader.Dispose();
			}

			assetsfileList.Clear();
			exportableAssets.Clear();
			visibleAssets.Clear();

			foreach (KeyValuePair<string, EndianBinaryReader> resourceFileReader in resourceFileReaders)
			{
				resourceFileReader.Value.Dispose();
			}

			resourceFileReaders.Clear();
			assetsFileIndexCache.Clear();
			productName = "";

			sceneTreeView?.Nodes.Clear();

			assetListView.VirtualListSize = 0;
			assetListView.Items.Clear();

			classesListView.Items.Clear();
			classesListView.Groups.Clear();

			previewPanel.BackgroundImage = Resources.preview;
			previewPanel.BackgroundImageLayout = ImageLayout.Center;
			assetInfoLabel.Visible = false;
			assetInfoLabel.Text = null;
			textPreviewBox.Visible = false;
			fontPreviewBox.Visible = false;
			glControl1.Visible = false;
			lastSelectedItem = null;
			lastLoadedAsset = null;
			firstSortColumn = -1;
			secondSortColumn = 0;
			reverseSort = false;
			enableFiltering = false;
			listSearch.Text = " Filter ";

			int count = filterTypeToolStripMenuItem.DropDownItems.Count;

			for (var i = 1; i < count; i++)
			{
				filterTypeToolStripMenuItem.DropDownItems.RemoveAt(1);
			}

			FMODreset();

			LoadedModuleDic.Clear();
			treeNodeCollection.Clear();
			treeNodeDictionary.Clear();
		}

		private void assetListView_MouseClick(object sender, MouseEventArgs e)
		{
			if (e.Button != MouseButtons.Right || this.assetListView.SelectedIndices.Count <= 0)
			{
				return;
			}

			this.jumpToSceneHierarchyToolStripMenuItem.Visible = false;
			this.showOriginalFileToolStripMenuItem.Visible = false;
			this.exportAnimatorWithSelectedAnimationClipMenuItem.Visible = false;
			this.exportObjectsWithSelectedAnimationClipMenuItem.Visible = false;

			if (this.assetListView.SelectedIndices.Count == 1)
			{
				this.jumpToSceneHierarchyToolStripMenuItem.Visible = true;
				this.showOriginalFileToolStripMenuItem.Visible = true;
			}

			if (this.assetListView.SelectedIndices.Count >= 1)
			{
				List<AssetPreloadData> selectedAssets = this.GetSelectedAssets();
				if (selectedAssets.Any(x => x.Type == ClassIDType.Animator) && selectedAssets.Any(x => x.Type == ClassIDType.AnimationClip))
				{
					this.exportAnimatorWithSelectedAnimationClipMenuItem.Visible = true;
				}
				else if (selectedAssets.All(x => x.Type == ClassIDType.AnimationClip))
				{
					this.exportObjectsWithSelectedAnimationClipMenuItem.Visible = true;
				}
			}

			this.contextMenuStrip1.Show(this.assetListView, e.X, e.Y);
		}

		private void exportSelectedAssetsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var saveFolderDialog1 = new OpenFolderDialog();

			if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			this.timer.Stop();

			ExportAssets(saveFolderDialog1.Folder, this.GetSelectedAssets(), this.assetGroupOptions.SelectedIndex, this.openAfterExport.Checked);
		}

		private void exportSelectedAssetsToRawToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var saveFolderDialog1 = new OpenFolderDialog();

			if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			this.timer.Stop();

			ExportAssets(saveFolderDialog1.Folder, this.GetSelectedAssets(), this.assetGroupOptions.SelectedIndex, this.openAfterExport.Checked, true);
		}

		private void showOriginalFileToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selectasset = (AssetPreloadData) assetListView.Items[assetListView.SelectedIndices[0]];
			string args = $"/select, \"{selectasset.sourceFile.parentPath ?? selectasset.sourceFile.filePath}\"";
			var pfi = new ProcessStartInfo("explorer.exe", args);
			Process process = Process.Start(pfi);
			process?.Dispose();
		}

		private void exportAnimatorwithAnimationClipMenuItem_Click(object sender, EventArgs e)
		{
			AssetPreloadData animator = null;

			var animationList = new List<AssetPreloadData>();

			List<AssetPreloadData> selectedAssets = GetSelectedAssets();

			foreach (AssetPreloadData assetPreloadData in selectedAssets)
			{
				if (assetPreloadData.Type == ClassIDType.Animator)
				{
					animator = assetPreloadData;
				}
				else if (assetPreloadData.Type == ClassIDType.AnimationClip)
				{
					animationList.Add(assetPreloadData);
				}
			}

			if (animator == null)
			{
				return;
			}

			var saveFolderDialog1 = new OpenFolderDialog();

			if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
			{
				return;
			}

			string exportPath = saveFolderDialog1.Folder + "\\Animator\\";
			this.progressBar1.Value = 0;
			this.progressBar1.Maximum = 1;

			ExportAnimatorWithAnimationClip(animator, animationList, exportPath);
		}

		private void exportSelectedObjectsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var saveFolderDialog1 = new OpenFolderDialog();

				if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				string exportPath = saveFolderDialog1.Folder + "\\GameObject\\";

				ExportObjectsWithAnimationClip(exportPath, this.sceneTreeView.Nodes);
			}
			else
			{
				StatusStripUpdate("No Objects available for export");
			}
		}

		private void exportObjectswithAnimationClipMenuItem_Click(object sender, EventArgs e)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var saveFolderDialog1 = new OpenFolderDialog();

				if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				string exportPath = saveFolderDialog1.Folder + "\\GameObject\\";

				List<AssetPreloadData> animationList = this.GetSelectedAssets().Where(x => x.Type == ClassIDType.AnimationClip).ToList();

				ExportObjectsWithAnimationClip(exportPath, this.sceneTreeView.Nodes, animationList.Count == 0 ? null : animationList);
			}
			else
			{
				StatusStripUpdate("No Objects available for export");
			}
		}

		private void listSearch_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyData == Keys.Enter)
			{
				this.FilterAssetList();
			}
		}

		private void liveSearch_CheckedChanged(object sender, EventArgs e)
		{
			if (enableLiveSearch.Checked)
			{
				FilterAssetList();
			}

			Settings.Default["enableLiveSearch"] = enableLiveSearch.Checked;
			Settings.Default.Save();
		}

		private void jumpToSceneHierarchyToolStripMenuItem_Click(object sender, EventArgs e)
		{
			var selectasset = (AssetPreloadData) assetListView.Items[assetListView.SelectedIndices[0]];

			if (selectasset.gameObject == null)
			{
				return;
			}

			this.sceneTreeView.SelectedNode = treeNodeDictionary[selectasset.gameObject];
			this.tabControl1.SelectedTab = this.tabPage1;
		}

		private void selectAllToolStripMenuItem_Click(object sender, EventArgs e)
		{
			StudioClasses.NativeMethods.SelectAllItems(this.assetListView);
		}

		private void assetListView_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.Control && e.KeyCode == Keys.A)
			{
				StudioClasses.NativeMethods.SelectAllItems(this.assetListView);
			}
		}

		private void textPreviewBox_KeyDown(object sender, KeyEventArgs e)
		{
			if (!e.Control || e.KeyCode != Keys.A)
			{
				return;
			}

			this.textPreviewBox.SelectAll();
			this.textPreviewBox.Focus();
		}

		private void contextMenuStrip1_Opening(object sender, System.ComponentModel.CancelEventArgs e)
		{
			if (!this.assetListView.Focused)
			{
				return;
			}

			int selectedCount = assetListView.SelectedIndices.Count;

			this.exportSelectedAssetsToolStripMenuItem.Enabled = selectedCount > 0;
			this.exportSelectedAssetsToRawToolStripMenuItem.Enabled = selectedCount > 0;

			this.exportSelectedAssetsToolStripMenuItem.Text = selectedCount == 1 ? Resources.ContextMenu_ExportSelectedAsset : Resources.ContextMenu_ExportSelectedAssets;
			this.exportSelectedAssetsToRawToolStripMenuItem.Text = selectedCount == 1 ? Resources.ContextMenu_ExportSelectedAssetRaw : Resources.ContextMenu_ExportSelectedAssetsRaw;

			string itemSelectedFormat = selectedCount == 1 ? Resources.ContextMenu_ItemSelectedFormat : Resources.ContextMenu_ItemsSelectedFormat;

			this.exportSelectedAssetsToolStripMenuItem.ShortcutKeyDisplayString = string.Format(itemSelectedFormat, selectedCount);
		}

		private void exportAllObjectssplitToolStripMenuItem1_Click(object sender, EventArgs e)
		{
			if (sceneTreeView.Nodes.Count > 0)
			{
				var saveFolderDialog1 = new OpenFolderDialog();

				if (saveFolderDialog1.ShowDialog(this) != DialogResult.OK)
				{
					return;
				}

				string savePath = saveFolderDialog1.Folder + "\\";
				this.progressBar1.Value = 0;
				this.progressBar1.Maximum = this.sceneTreeView.Nodes.Cast<TreeNode>().Sum(x => x.Nodes.Count);

				ExportSplitObjects(savePath, this.sceneTreeView.Nodes);
			}
			else
			{
				StatusStripUpdate("No Objects available for export");
			}
		}

		private List<AssetPreloadData> GetSelectedAssets()
		{
			var selectedAssets = new List<AssetPreloadData>();
			foreach (int index in assetListView.SelectedIndices)
			{
				selectedAssets.Add((AssetPreloadData) assetListView.Items[index]);
			}

			return selectedAssets;
		}

		private static List<AssetPreloadData> ExecuteFilterQuery(string query)
		{
			// ReSharper disable once InvertIf
			if (query.ToLowerInvariant().StartsWith("type:"))
			{
				string typeQuery = query.Remove(0, 5);

				if (typeQuery != string.Empty)
				{
					return visibleAssets.FindAll(x => x.TypeString.IndexOf(typeQuery, StringComparison.CurrentCultureIgnoreCase) >= 0);
				}
			}

			return visibleAssets.FindAll(x => x.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0);
		}

		private void FilterAssetList()
		{
			assetListView.BeginUpdate();
			assetListView.SelectedIndices.Clear();

			var show = new List<ClassIDType>();

			if (!allToolStripMenuItem.Checked)
			{
				for (var i = 1; i < filterTypeToolStripMenuItem.DropDownItems.Count; i++)
				{
					var item = (ToolStripMenuItem) filterTypeToolStripMenuItem.DropDownItems[i];

					if (item.Checked)
					{
						show.Add((ClassIDType) Enum.Parse(typeof(ClassIDType), item.Text));
					}
				}

				visibleAssets = exportableAssets.FindAll(x => show.Contains(x.Type));
			}
			else
			{
				visibleAssets = exportableAssets;
			}

			if (listSearch.Text != " Filter ")
			{
				visibleAssets = ExecuteFilterQuery(listSearch.Text);
			}

			assetListView.VirtualListSize = visibleAssets.Count;
			assetListView.EndUpdate();
		}

		private void fontPreviewBox_VisibleChanged(object sender, EventArgs e)
		{
			if (!this.fontPreviewBox.Visible)
			{
				this.fontPreviewBox.SelectionFont.Dispose();
			}
		}
	}
}
