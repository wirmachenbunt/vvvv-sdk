#region usings
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

using Microsoft.Practices.Unity;
using VVVV.Core;
using VVVV.Core.Logging;
using VVVV.Core.Menu;
using VVVV.Core.View;
using VVVV.HDE.Viewer;
using VVVV.HDE.Viewer.WinFormsViewer;
using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;

#endregion usings

namespace VVVV.Nodes.Finder
{
	public enum SearchScope {Local, Downstream, Global};
	
	public struct Filter
	{
		public SearchScope Scope;
		public bool SendReceive;
		public bool Comments;
		public bool Labels;
		public bool Effects;
		public bool Freeframes;
		public bool Modules;
		public bool Plugins;
		public bool IONodes;
		public bool Natives;
		public bool VSTs;
		public bool Patches;
		public bool Unknowns;
		public bool Boygrouped;
		public bool Addons;
		public bool Windows;
		public bool IDs;
		private List<string> FTags;
		public List<string> Tags
		{
			get {return FTags;}
			set
			{
				FTags = value;
				
				if (FTags .Contains("g"))
				{
					Scope = SearchScope.Global;
					FTags .Remove("g");
				}
				else if (FTags .Contains("d"))
				{
					Scope = SearchScope.Downstream;
					FTags .Remove("d");
				}
				
				if (FTags.Contains("s"))
				{
					SendReceive = true;
					FTags.Remove("s");
				}
				if (FTags.Contains("/"))
				{
					Comments = true;
					FTags.Remove("/");
				}
				if (FTags.Contains("l"))
				{
					Labels = true;
					FTags.Remove("l");
				}
				if (FTags.Contains("x"))
				{
					Effects = true;
					FTags.Remove("x");
				}
				if (FTags.Contains("f"))
				{
					Freeframes = true;
					FTags.Remove("f");
				}
				if (FTags.Contains("m"))
				{
					Modules = true;
					FTags.Remove("m");
				}
				if (FTags.Contains("p"))
				{
					Plugins = true;
					FTags.Remove("p");
				}
				if (FTags.Contains("i"))
				{
					IONodes = true;
					FTags.Remove("i");
				}
				if (FTags.Contains("n"))
				{
					Natives = true;
					FTags.Remove("n");
				}
				if (FTags.Contains("v"))
				{
					VSTs = true;
					FTags.Remove("v");
				}
				if (FTags.Contains("t"))
				{
					Patches = true;
					FTags.Remove("t");
				}
				if (FTags.Contains("r"))
				{
					Unknowns = true;
					FTags.Remove("r");
				}
				if (FTags.Contains("a"))
				{
					Addons = true;
					FTags.Remove("a");
				}
				if (FTags.Contains("b"))
				{
					Boygrouped = true;
					FTags.Remove("b");
				}
				if (FTags.Contains("w"))
				{
					Windows = true;
					FTags.Remove("w");
				}
				if (FTags.Contains("#"))
				{
					IDs = true;
					FTags.Remove("#");
				}
				
				//clean up the list
				FTags[FTags.Count-1] = FTags[FTags.Count-1].Trim((char) 160);
				while (FTags.Contains(" "))
					FTags.Remove(" ");
				if (FTags.Contains(""))
					FTags.Remove("");
			}
		}
		
		public bool QuickTagsUsed()
		{
			return SendReceive || Comments || Labels || IONodes || Natives || Modules || Effects || Freeframes || VSTs || Plugins || Patches || Unknowns || Addons || Boygrouped || Windows || IDs;
		}
	}
	
	[PluginInfo(Name = "Finder",
	            Category = "HDE",
	            Shortcut = "Ctrl+F",
	            Author = "vvvv group",
	            Help = "Finds Nodes, Comments and Send/Receive channels and more.",
	            InitialBoxWidth = 200,
	            InitialBoxHeight = 250,
	            InitialWindowWidth = 320,
	            InitialWindowHeight = 500,
	            InitialComponentMode = TComponentMode.InAWindow)]
	public class FinderPluginNode: UserControl, IPluginHDE, INodeListener
	{
		#region field declaration
		private IDiffSpread<string> FTagsPin;
		
		private IPluginHost2 FPluginHost;
		private IHDEHost FHDEHost;
		private MappingRegistry FMappingRegistry;
		private List<PatchNode> FPlainResultList = new List<PatchNode>();
		
		private IWindow FActivePatchWindow;
		private IWindow FActiveWindow;
		private INode FActivePatchNode;
		private INode FActivePatchParent;
		private PatchNode FSearchResult;
		
		private Filter FFilter;
		private int FSearchIndex;
		
		// Track whether Dispose has been called.
		private bool FDisposed = false;
		
		[Import]
		ILogger FLogger;
		#endregion field declaration
		
		#region constructor/destructor
		[ImportingConstructor]
		public FinderPluginNode(IHDEHost host, IPluginHost2 pluginHost, [Config("Tags")] IDiffSpread<string> tagsPin)
		{
			// The InitializeComponent() call is required for Windows Forms designer support.
			InitializeComponent();
			
			FHDEHost = host;
			FPluginHost = pluginHost;
			
			FSearchTextBox.ContextMenu = new ContextMenu();
			FSearchTextBox.ContextMenu.Popup += FSearchTextBox_ContextMenu_Popup;
			FSearchTextBox.MouseWheel += FSearchTextBox_MouseWheel;
			
			FMappingRegistry = new MappingRegistry();
			FMappingRegistry.RegisterDefaultMapping<INamed, DefaultNameProvider>();
			FHierarchyViewer.Registry = FMappingRegistry;
			
			FHDEHost.WindowSelectionChanged += FHDEHost_WindowSelectionChanged;
			//defer setting the active patch window as
			//this will trigger the initial WindowSelectionChangeCB
			//which will want to access this windows caption which is not yet available
			SynchronizationContext.Current.Post((object state) => FHDEHost_WindowSelectionChanged(FHDEHost, new WindowEventArgs(FHDEHost.ActivePatchWindow)), null);
			
			FTagsPin = tagsPin;
			FTagsPin.Changed += FTagsPin_Changed;
		}

		private void InitializeComponent()
		{
			this.panel1 = new System.Windows.Forms.Panel();
			this.panel3 = new System.Windows.Forms.Panel();
			this.FSearchTextBox = new System.Windows.Forms.TextBox();
			this.panel2 = new System.Windows.Forms.Panel();
			this.FNodeCountLabel = new System.Windows.Forms.Label();
			this.FHierarchyViewer = new VVVV.HDE.Viewer.WinFormsViewer.HierarchyViewer();
			this.panel1.SuspendLayout();
			this.panel3.SuspendLayout();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.panel1.Controls.Add(this.panel3);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(252, 17);
			this.panel1.TabIndex = 7;
			// 
			// panel3
			// 
			this.panel3.Controls.Add(this.FSearchTextBox);
			this.panel3.Controls.Add(this.panel2);
			this.panel3.Dock = System.Windows.Forms.DockStyle.Fill;
			this.panel3.Location = new System.Drawing.Point(0, 0);
			this.panel3.Name = "panel3";
			this.panel3.Size = new System.Drawing.Size(250, 15);
			this.panel3.TabIndex = 11;
			// 
			// FSearchTextBox
			// 
			this.FSearchTextBox.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(240)))), ((int)(((byte)(240)))));
			this.FSearchTextBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.FSearchTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
			this.FSearchTextBox.Location = new System.Drawing.Point(2, 0);
			this.FSearchTextBox.MinimumSize = new System.Drawing.Size(0, 17);
			this.FSearchTextBox.Name = "FSearchTextBox";
			this.FSearchTextBox.Size = new System.Drawing.Size(248, 17);
			this.FSearchTextBox.TabIndex = 13;
			this.FSearchTextBox.TextChanged += new System.EventHandler(this.FSearchTextBoxTextChanged);
			this.FSearchTextBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.FSearchTextBoxKeyDown);
			// 
			// panel2
			// 
			this.panel2.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(240)))), ((int)(((byte)(240)))));
			this.panel2.Dock = System.Windows.Forms.DockStyle.Left;
			this.panel2.Location = new System.Drawing.Point(0, 0);
			this.panel2.Name = "panel2";
			this.panel2.Size = new System.Drawing.Size(2, 15);
			this.panel2.TabIndex = 11;
			// 
			// FNodeCountLabel
			// 
			this.FNodeCountLabel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(240)))), ((int)(((byte)(240)))));
			this.FNodeCountLabel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
			this.FNodeCountLabel.Dock = System.Windows.Forms.DockStyle.Bottom;
			this.FNodeCountLabel.Location = new System.Drawing.Point(0, 256);
			this.FNodeCountLabel.Name = "FNodeCountLabel";
			this.FNodeCountLabel.Size = new System.Drawing.Size(252, 17);
			this.FNodeCountLabel.TabIndex = 8;
			this.FNodeCountLabel.Text = "Matching Nodes: ";
			// 
			// FHierarchyViewer
			// 
			this.FHierarchyViewer.Dock = System.Windows.Forms.DockStyle.Fill;
			this.FHierarchyViewer.Location = new System.Drawing.Point(0, 17);
			this.FHierarchyViewer.Name = "FHierarchyViewer";
			this.FHierarchyViewer.ShowLinks = false;
			this.FHierarchyViewer.Size = new System.Drawing.Size(252, 239);
			this.FHierarchyViewer.TabIndex = 9;
			this.FHierarchyViewer.DoubleClick += new VVVV.HDE.Viewer.WinFormsViewer.ClickHandler(this.FHierarchyViewerDoubleClick);
			this.FHierarchyViewer.Click += new VVVV.HDE.Viewer.WinFormsViewer.ClickHandler(this.FHierarchyViewerClick);
			this.FHierarchyViewer.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.FHierarchyViewerKeyPress);
			// 
			// FinderPluginNode
			// 
			this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(224)))), ((int)(((byte)(224)))), ((int)(((byte)(224)))));
			this.Controls.Add(this.FHierarchyViewer);
			this.Controls.Add(this.FNodeCountLabel);
			this.Controls.Add(this.panel1);
			this.DoubleBuffered = true;
			this.Name = "FinderPluginNode";
			this.Size = new System.Drawing.Size(252, 273);
			this.panel1.ResumeLayout(false);
			this.panel3.ResumeLayout(false);
			this.panel3.PerformLayout();
			this.ResumeLayout(false);
		}
		private System.Windows.Forms.Panel panel2;
		private System.Windows.Forms.Panel panel3;
		private VVVV.HDE.Viewer.WinFormsViewer.HierarchyViewer FHierarchyViewer;
		private System.Windows.Forms.Label FNodeCountLabel;
		private System.Windows.Forms.TextBox FSearchTextBox;
		private System.Windows.Forms.Panel panel1;
		
		// Dispose(bool disposing) executes in two distinct scenarios.
		// If disposing equals true, the method has been called directly
		// or indirectly by a user's code. Managed and unmanaged resources
		// can be disposed.
		// If disposing equals false, the method has been called by the
		// runtime from inside the finalizer and you should not reference
		// other objects. Only unmanaged resources can be disposed.
		protected override void Dispose(bool disposing)
		{
			// Check to see if Dispose has already been called.
			if(!FDisposed)
			{
				if(disposing)
				{
					// Dispose managed resources.
					FSearchTextBox.ContextMenu.Popup -= FSearchTextBox_ContextMenu_Popup;
					FSearchTextBox.MouseWheel -= FSearchTextBox_MouseWheel;
					FHDEHost.WindowSelectionChanged -= FHDEHost_WindowSelectionChanged;
					FTagsPin.Changed -= FTagsPin_Changed;
					
					if (FSearchResult != null)
						FSearchResult.Dispose();
					
					FActivePatchNode = null;
					
					this.FSearchTextBox.TextChanged -= this.FSearchTextBoxTextChanged;
					this.FSearchTextBox.KeyDown -= this.FSearchTextBoxKeyDown;
					
					this.FHierarchyViewer.DoubleClick -= this.FHierarchyViewerDoubleClick;
					this.FHierarchyViewer.Click -= this.FHierarchyViewerClick;
					this.FHierarchyViewer.KeyPress -= this.FHierarchyViewerKeyPress;
					this.FHierarchyViewer.Dispose();
					this.FHierarchyViewer = null;
					
					FMappingRegistry.Container.Dispose();
				}
				// Release unmanaged resources. If disposing is false,
				// only the following code is executed.
				
				// Note that this is not thread safe.
				// Another thread could start disposing the object
				// after the managed resources are disposed,
				// but before the disposed flag is set to true.
				// If thread safety is necessary, it must be
				// implemented by the client.
			}
			FDisposed = true;
		}
		
		#endregion constructor/destructor
		
		void FTagsPin_Changed(IDiffSpread<string> spread)
		{
			FSearchTextBox.Text = spread[0];
		}
		
		#region IWindowSelectionListener
		void FHDEHost_WindowSelectionChanged(object sender, WindowEventArgs args)
		{
			var window = args.Window;
			
			if (window == FActiveWindow)
				return;
			
			FActiveWindow = window;
			var windowType = window.GetWindowType();
			var updateActiveWindow = false;
			
			if (windowType == WindowType.Module || windowType == WindowType.Patch)
			{
				if (window != FActivePatchWindow)
					SetActivePatch(window);
				else
					updateActiveWindow = true;
			}
			else
			{
				if (FHDEHost.ActivePatchWindow == null)
				{
					ClearSearch();
					FActivePatchNode = null;
					if (FActivePatchParent != null)
						FActivePatchParent.RemoveListener(this);
				}
				else if (FActivePatchWindow != FHDEHost.ActivePatchWindow)
					SetActivePatch(FHDEHost.ActivePatchWindow);
					
				updateActiveWindow = true;
			}
			
			if (updateActiveWindow && FSearchResult != null)
			{
				FSearchResult.SetActiveWindow(FActiveWindow);
				FHierarchyViewer.Redraw();
			}
		}
		
		private void SetActivePatch(IWindow patch)
		{
			if (FActivePatchParent != null)
				FActivePatchParent.RemoveListener(this);
			
			FActivePatchNode = patch.GetNode();
			FActivePatchWindow = patch;
			
			//the hosts window may be null if the plugin is created hidden on startup
			if (FPluginHost.Window != null)
				FPluginHost.Window.Caption = FActivePatchNode.GetNodeInfo().Systemname;
			
			FActivePatchParent = FindParent(FHDEHost.Root, FActivePatchNode);
			//if this is not the root
			if (FActivePatchParent != null)
				FActivePatchParent.AddListener(this);
			
			UpdateSearch();
		}
		#endregion IWindowSelectionListener
		
		#region INodeListener
		public void AddedCB(INode childNode)
		{
			//do nothing;
		}
		
		public void RemovedCB(INode childNode)
		{
			//if active patch is being deleted
			//detach view
			if (childNode == FActivePatchNode)
			{
				FActivePatchNode = null;
				FActivePatchWindow = null;
				FActiveWindow = null;
				
				if (FActivePatchParent != null)
				{
					FActivePatchParent.RemoveListener(this);
					FActivePatchParent = null;
				}
				
				if (FFilter.Scope != SearchScope.Global)
					ClearSearch();
			}
		}
		
		public void LabelChangedCB()
		{
			//do nothing
		}
		#endregion INodeListener
		
		#region Search
		void FSearchTextBoxTextChanged(object sender, EventArgs e)
		{
			UpdateSearch();
			
			//save tags in config pin
			FTagsPin[0] = FSearchTextBox.Text;
		}
		
		void FSearchTextBoxKeyDown(object sender, KeyEventArgs e)
		{
			if (FPlainResultList.Count == 0)
				return;
			
			if (e.KeyCode == Keys.F3 || e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
			{
				FPlainResultList[FSearchIndex].Selected = false;
				if (e.Shift || e.KeyCode == Keys.Up)
				{
					FSearchIndex -= 1;
					if (FSearchIndex < 0)
						FSearchIndex = FPlainResultList.Count - 1;
				}
				else
					FSearchIndex = (FSearchIndex + 1) % FPlainResultList.Count;
				
				FPlainResultList[FSearchIndex].Selected = true;
				
				//select the node
				FHDEHost.SelectNodes(new INode[1]{FPlainResultList[FSearchIndex].Node});
				
				FHierarchyViewer.Redraw();
			}
			else if (e.KeyCode == Keys.Return || e.KeyCode == Keys.Enter)
			{
				OpenPatch(FPlainResultList[FSearchIndex].Node);
			}
		}
		
		private void ClearSearch()
		{
			if (FSearchResult != null)
				FSearchResult.Dispose();
			FPlainResultList.Clear();
			FSearchIndex = 0;
		}
		
		private void UpdateSearch()
		{
			if (FHDEHost.Root == null)
				return;
			
			string query = FSearchTextBox.Text.ToLower();
			query += (char) 160;
			
			FFilter = new Filter();
			FFilter.Tags = query.Split(new char[1]{' '}).ToList();
			FHierarchyViewer.ShowLinks = FFilter.SendReceive;
			
			FHierarchyViewer.BeginUpdate();
			try
			{
				ClearSearch();
				
				switch (FFilter.Scope)
				{
					case SearchScope.Global:
						{
							FSearchResult = new PatchNode(FHDEHost.Root, FFilter, true, true);
							FHierarchyViewer.ShowRoot = false;
							break;
						}
					case SearchScope.Local:
						{
							FSearchResult = new PatchNode(FActivePatchNode, FFilter, true, false);
							FHierarchyViewer.ShowRoot = true;
							break;
						}
					case SearchScope.Downstream:
						{
							FSearchResult = new PatchNode(FActivePatchNode, FFilter, true, true);
							FHierarchyViewer.ShowRoot = true;
							break;
						}
				}
				
				FSearchResult.SetActiveWindow(FActiveWindow);

				FHierarchyViewer.Input = FSearchResult;

				FNodeCountLabel.Text = "Matching Nodes: " + FPlainResultList.Count.ToString();
			}
			finally
			{
				FHierarchyViewer.EndUpdate();
			}
		}
		#endregion Search
		
		#region GUI events
		void FSearchTextBox_MouseWheel(object sender, System.Windows.Forms.MouseEventArgs e)
		{
			FHierarchyViewer.Focus();
		}

		void FSearchTextBox_ContextMenu_Popup(object sender, EventArgs e)
		{
			FSearchTextBox.Text = "";
		}
		
		void FHierarchyViewerClick(IModelMapper sender, System.Windows.Forms.MouseEventArgs e)
		{
		    if (e.Button == 0 && sender.Model != null)
			{
			    
				(sender.Model as PatchNode).Selected = true;
				FHDEHost.SelectNodes(new INode[1]{(sender.Model as PatchNode).Node});
				
				//only fit view to selected node if not in local scope
				if (FFilter.Scope != SearchScope.Local)
					if (sender.CanMap<ICamera>())
				{
				    var parent = FindParent(FHDEHost.Root, (sender.Model as PatchNode).Node);
				    if (parent == null)
				        parent = FHDEHost.Root;
    				
				    sender.Map<ICamera>().View(FSearchResult.FindNode(parent));
				}
			}
			else if ((int)e.Button == 1)
			{
				if (sender.CanMap<ICamera>())
					sender.Map<ICamera>().ViewAll();
			}
			else if ((int)e.Button == 2 && sender.Model != null)
			{
			    OpenPatch((sender.Model as PatchNode).Node);
			}
		}
		
		void FHierarchyViewerDoubleClick(IModelMapper sender, System.Windows.Forms.MouseEventArgs e)
		{
			//OpenPatch((sender.Model as PatchNode).Node);
		}
		
		void FHierarchyViewerKeyPress(object sender, KeyPressEventArgs e)
		{
			FSearchTextBox.Focus();
			FSearchTextBox.Text += (e.KeyChar).ToString();
			FSearchTextBox.Select(FSearchTextBox.Text.Length, 1);
		}
		#endregion GUI events
		
		private void OpenPatch(INode node)
		{
			if (node == null)
				FHDEHost.ShowEditor(FindParent(FHDEHost.Root, FActivePatchNode));
			else if (node.HasPatch())
				FHDEHost.ShowEditor(node);
			else
			{
				FHDEHost.ShowEditor(FindParent(FHDEHost.Root, node));
				FHDEHost.SelectNodes(new INode[1]{node});
			}
		}
		
		private INode FindParent(INode sourceTree, INode node)
		{
			INode[] children = sourceTree.GetChildren();
			
			if (children != null)
			{
				foreach(INode child in children)
				{
					if (child == node)
						return sourceTree;
					else
					{
						INode p = FindParent(child, node);
						if (p != null)
							return p;
					}
				}
				return null;
			}
			else
				return null;
		}
	}
}
