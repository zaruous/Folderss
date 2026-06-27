using AvalonDock;
using AvalonDock.Layout;
using Folderss.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Folderss.LayoutTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var testDirectory = Path.Combine(Path.GetTempPath(), "Folderss.LayoutTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);

            try
            {
                var firstPath = Path.Combine(testDirectory, "first.xml");
                var secondPath = Path.Combine(testDirectory, "second.xml");
                var source = CreateManager(CreateExpectedLayout());

                AssertExpectedPanelRelationships(source.Layout, "source");
                DockLayoutService.Save(source, firstPath);
                Assert(File.ReadAllText(firstPath + ".version").Trim() == "2", "Layout version was not saved.");

                var contents = CreateContents();
                var restored = CreateManager(CreateInitialLayout(contents));
                var host = CreateHost(restored);
                host.Show();
                host.UpdateLayout();

                Assert(
                    DockLayoutService.Restore(restored, id => contents.TryGetValue(id, out var content) ? content : null, firstPath),
                    "Restore returned false.");
                host.UpdateLayout();
                AssertExpectedPanelRelationships(restored.Layout, "restored");

                DockLayoutService.Save(restored, secondPath);
                host.Close();

                var secondContents = CreateContents();
                var reloaded = CreateManager(CreateInitialLayout(secondContents));
                Assert(
                    DockLayoutService.Restore(reloaded, id => secondContents.TryGetValue(id, out var content) ? content : null, secondPath),
                    "Second restore returned false.");
                AssertExpectedPanelRelationships(reloaded.Layout, "reloaded");

                Console.WriteLine("PASS: separate lower console pane survived save, restore, and re-save.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex);
                return 1;
            }
            finally
            {
                try
                {
                    Directory.Delete(testDirectory, true);
                }
                catch
                {
                }
            }
        }

        private static DockingManager CreateManager(LayoutRoot layout)
        {
            return new DockingManager
            {
                AllowMixedOrientation = true,
                Layout = layout
            };
        }

        private static LayoutRoot CreateExpectedLayout()
        {
            var favoritesPane = new LayoutAnchorablePane { DockWidth = new GridLength(230) };
            favoritesPane.Children.Add(new LayoutAnchorable
            {
                Title = "Favorites",
                ContentId = "favorites",
                Content = new Border()
            });

            var leftPane = CreateDocumentPane("Left", "left-folder", new Border());
            leftPane.Children.Add(new LayoutDocument
            {
                Title = "Add panel",
                ContentId = "add-folder-panel",
                Content = new Grid(),
                CanClose = false
            });

            var consolePane = CreateDocumentPane("Console", "console", new Border());
            consolePane.DockHeight = new GridLength(220);
            var rightPane = CreateDocumentPane("Right", "right-folder", new Border());

            var leftColumn = new LayoutDocumentPaneGroup
            {
                Orientation = Orientation.Vertical,
                DockWidth = new GridLength(5, GridUnitType.Star)
            };
            leftColumn.Children.Add(leftPane);
            leftColumn.Children.Add(consolePane);

            var documents = new LayoutDocumentPaneGroup { Orientation = Orientation.Horizontal };
            documents.Children.Add(leftColumn);
            documents.Children.Add(rightPane);

            var rootPanel = new LayoutPanel { Orientation = Orientation.Horizontal };
            rootPanel.Children.Add(favoritesPane);
            rootPanel.Children.Add(documents);
            return new LayoutRoot { RootPanel = rootPanel };
        }

        private static Dictionary<string, object> CreateContents()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["favorites"] = new Border(),
                ["left-folder"] = new Border(),
                ["add-folder-panel"] = new Grid(),
                ["console"] = new Border(),
                ["right-folder"] = new Border()
            };
        }

        private static LayoutRoot CreateInitialLayout(IReadOnlyDictionary<string, object> contents)
        {
            var favoritesPane = new LayoutAnchorablePane { DockWidth = new GridLength(230) };
            favoritesPane.Children.Add(new LayoutAnchorable
            {
                Title = "Favorites",
                ContentId = "favorites",
                Content = contents["favorites"]
            });

            var leftPane = CreateDocumentPane("Left", "left-folder", contents["left-folder"]);
            leftPane.Children.Add(new LayoutDocument
            {
                Title = "Console",
                ContentId = "console",
                Content = contents["console"]
            });
            leftPane.Children.Add(new LayoutDocument
            {
                Title = "Add panel",
                ContentId = "add-folder-panel",
                Content = contents["add-folder-panel"]
            });
            var rightPane = CreateDocumentPane("Right", "right-folder", contents["right-folder"]);

            var documents = new LayoutDocumentPaneGroup { Orientation = Orientation.Horizontal };
            documents.Children.Add(leftPane);
            documents.Children.Add(rightPane);

            var row = new LayoutPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(favoritesPane);
            row.Children.Add(documents);
            var root = new LayoutPanel { Orientation = Orientation.Vertical };
            root.Children.Add(row);
            return new LayoutRoot { RootPanel = root };
        }

        private static Window CreateHost(DockingManager manager)
        {
            return new Window
            {
                Content = manager,
                Width = 800,
                Height = 600,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
        }

        private static LayoutDocumentPane CreateDocumentPane(string title, string contentId, object content)
        {
            var pane = new LayoutDocumentPane();
            pane.Children.Add(new LayoutDocument
            {
                Title = title,
                ContentId = contentId,
                Content = content,
                CanClose = false
            });
            return pane;
        }

        private static void AssertExpectedPanelRelationships(LayoutRoot layout, string stage)
        {
            var left = FindDocument(layout, "left-folder");
            var console = FindDocument(layout, "console");
            var right = FindDocument(layout, "right-folder");

            var leftPane = left.Parent as LayoutDocumentPane;
            var consolePane = console.Parent as LayoutDocumentPane;
            var rightPane = right.Parent as LayoutDocumentPane;

            Assert(leftPane != null, stage + ": left document pane is missing.");
            Assert(consolePane != null, stage + ": console document pane is missing.");
            Assert(rightPane != null, stage + ": right document pane is missing.");
            Assert(!ReferenceEquals(leftPane, consolePane), stage + ": console was merged into the left tab pane.");

            var leftColumn = leftPane.Parent as LayoutDocumentPaneGroup;
            Assert(leftColumn != null, stage + ": left column group is missing.");
            Assert(ReferenceEquals(leftColumn, consolePane.Parent), stage + ": console is not below the left pane.");
            Assert(leftColumn.Orientation == Orientation.Vertical, stage + ": left and console panes are not vertically split.");

            var documentRow = leftColumn.Parent as LayoutDocumentPaneGroup;
            Assert(documentRow != null, stage + ": document row group is missing.");
            Assert(ReferenceEquals(documentRow, rightPane.Parent), stage + ": right pane is not beside the left column.");
            Assert(documentRow.Orientation == Orientation.Horizontal, stage + ": left column and right pane are not horizontally split.");
        }

        private static LayoutDocument FindDocument(LayoutRoot layout, string contentId)
        {
            var document = layout.Descendents()
                .OfType<LayoutDocument>()
                .FirstOrDefault(item => string.Equals(item.ContentId, contentId, StringComparison.Ordinal));
            if (document == null)
                throw new InvalidOperationException("Document not found: " + contentId);
            return document;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
