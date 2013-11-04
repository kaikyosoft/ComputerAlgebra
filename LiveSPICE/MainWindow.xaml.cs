﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using Xceed.Wpf.AvalonDock.Layout;
using Microsoft.Win32;

namespace LiveSPICE
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ComponentLibrary Components { get { return (ComponentLibrary)components.Content; } }
        public PropertyGrid Properties { get { return (PropertyGrid)properties.Content; } }

        public MainWindow()
        {
            InitializeComponent();

            Components.Init(toolbox_Click, CommandBindings);
            Properties.PropertyValueChanged += properties_PropertyValueChanged;
            schematics.ChildrenTreeChanged += (o, e) => NotifyChanged("ActivewViewerZoom");
        }

        public IEnumerable<SchematicViewer> Viewers { get { return schematics.Children.Select(i => i.Content).OfType<SchematicViewer>(); } }
        public IEnumerable<SchematicEditor> Editors { get { return Viewers.Select(i => i.Schematic).OfType<SchematicEditor>(); } }

        public LayoutContent ActiveContent { get { return schematics.SelectedContent; } }
        public SchematicViewer ActiveViewer 
        { 
            get 
            {
                if (schematics == null) return null;
                LayoutContent selected = schematics.SelectedContent;
                return selected != null ? (SchematicViewer)selected.Content : null;
            } 
        }
        public SchematicEditor ActiveEditor 
        { 
            get 
            {
                SchematicViewer active = ActiveViewer;
                return active != null ? (SchematicEditor)active.Schematic : null;
            } 
        }

        public double ActiveViewerZoom { get { return ActiveViewer.Zoom; } set { ActiveViewer.Zoom = value; NotifyChanged("ActiveViewerZoom"); } }

        private string status;
        public string Status 
        {
            get { return status != null ? status : "Ready"; } 
            set { status = value; NotifyChanged("Status"); }
        }

        private SchematicViewer New(SchematicEditor Schematic)
        {
            Schematic.SelectionChanged += schematic_SelectionChanged;
            Schematic.EditSelection += schematic_EditSelection;

            SchematicViewer sv = new SchematicViewer(Schematic);
            LayoutDocument doc = new LayoutDocument()
            {
                Content = sv,
                Title = Schematic.Title,
                ToolTip = Schematic.FilePath,
                IsActive = true
            };
            doc.Closing += (o, e) => e.Cancel = !Schematic.CanClose();

            Schematic.PropertyChanged += (o, e) => 
            {
                if (e.PropertyName == "FilePath")
                {
                    doc.Title = Schematic.Title;
                    doc.ToolTip = Schematic.FilePath;
                }
            };

            sv.Tag = doc;

            schematics.Children.Add(doc);
            dock.UpdateLayout();
            sv.FocusCenter();
            return sv;
        }

        public SchematicViewer FindViewer(string FileName)
        {
            FileName = System.IO.Path.GetFullPath(FileName);
            foreach (SchematicViewer i in Viewers)
                if (System.IO.Path.GetFullPath(((SchematicEditor)i.Schematic).FilePath) == FileName)
                    return i;
            return null;
        }

        private void Open(string FileName)
        {
            SchematicViewer open = FindViewer(FileName);
            if (open != null)
            {
                ((LayoutDocument)open.Tag).IsSelected = true;
                // If this schematic is already open, prompt for re-open if necessary.
                if (((SchematicEditor)open.Schematic).CanClose(true))
                    open.Schematic = SchematicEditor.Open(FileName);
            }
            else
            {
                // Just make a new one.
                New(SchematicEditor.Open(FileName));
            }
        }

        private void New_Executed(object sender, ExecutedRoutedEventArgs e) { New(new SchematicEditor()); }
        private void OnMruClick(object sender, RoutedEventArgs e) { Open((string)((MenuItem)e.Source).Tag); }
        private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            try
            {
                OpenFileDialog d = new OpenFileDialog()
                {
                    Filter = "Circuit Schematics|*" + SchematicEditor.FileExtension,
                    DefaultExt = SchematicEditor.FileExtension,
                    Multiselect = true
                };
                if (d.ShowDialog(this) ?? false)
                {
                    foreach (string i in d.FileNames)
                        Open(i);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void SaveAll_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            foreach (SchematicViewer i in schematics.Children.Select(i => i.Content).OfType<SchematicViewer>())
                if (!((SchematicEditor)i.Schematic).Save())
                    break;
        }

        private void Close_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = ActiveViewer != null; }
        private void Close_Executed(object sender, ExecutedRoutedEventArgs e) { App.Current.Settings.MainWindowLayout = dock.SaveLayout(); ActiveContent.Close(); }
        
        private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) { Close(); }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            // Find the schematics that have pending edits.
            IEnumerable<SchematicEditor> dirty = Editors.Where(i => i.Edits.Dirty);
            if (!dirty.Any()) 
                return;

            if (!ClosingDialog.Show(this, dirty))
                e.Cancel = true;
        }

        private void schematic_SelectionChanged(object Sender, SelectionEventArgs Args)
        {
            Properties.SelectedObjects = Args.Selected.OfType<Circuit.Symbol>().Select(i => i.Component).ToArray<object>();
            Properties.Tag = ((SchematicEditor)Sender).Edits;
        }

        private void schematic_EditSelection(object sender, EventArgs e)
        {
            Properties.Focus();
        }

        void properties_PropertyValueChanged(object sender, PropertyValueChangedEventArgs e)
        {
            IEnumerable<object> selected = Properties.SelectedObjects;
            PropertyInfo property = selected.First().GetType().GetProperty(e.ChangedItem.PropertyDescriptor.Name);
            EditStack edits = (EditStack)Properties.Tag;
            edits.Did(EditList.New(selected.Select(i => new PropertyEdit(i, property, property.GetValue(i), e.OldValues[i]))));
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                dock.LoadLayout(App.Current.Settings.MainWindowLayout);
            }
            catch (System.Exception)
            {
            }
        }

        private void toolbox_Click(object s, RoutedEventArgs e) 
        {
            SchematicEditor active = ActiveEditor;
            if (active == null)
                return;

            e.Handled = true;

            Type type = (Type)((Button)s).Tag;
            if (type == typeof(Circuit.Conductor))
                active.Tool = new WireTool(active);
            else
                active.Tool = new SymbolTool(active, type);

            active.Focus();
            Keyboard.Focus(active);
        }
        
        private void Simulate_CanExecute(object sender, CanExecuteRoutedEventArgs e) { e.CanExecute = ActiveEditor != null; }
        private void Simulate_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ActiveEditor != null)
            {
                AudioConfig config = new AudioConfig() { Owner = this };
                if (config.Inputs.Length + config.Outputs.Length == 0)
                    if (!(config.ShowDialog() ?? false))
                        return;

                LiveSimulation simulation = new LiveSimulation(ActiveEditor.Schematic, config.Device, config.Inputs, config.Outputs) { Owner = this };
                simulation.Show();
            }
        }

        private void AudioConfiguration_Click(object sender, RoutedEventArgs e)
        {
            AudioConfig config = new AudioConfig() { Owner = this };
            config.ShowDialog();
        }

        private void ViewProperties_Click(object sender, RoutedEventArgs e) { ToggleVisible(properties); }
        private void ViewComponents_Click(object sender, RoutedEventArgs e) { ToggleVisible(components); }

        private static void ToggleVisible(LayoutAnchorable Anchorable)
        {
            if (Anchorable.IsVisible)
                Anchorable.Hide();
            else
                Anchorable.Show();
        }

        // INotifyPropertyChanged.
        private void NotifyChanged(string p)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(p));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
