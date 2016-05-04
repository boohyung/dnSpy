﻿/*
    Copyright (C) 2014-2016 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using dnSpy.Contracts.Plugin;
using dnSpy.Contracts.TextEditor;
using dnSpy.Contracts.Themes;
using dnSpy.Decompiler.Shared;
using dnSpy.Shared.AvalonEdit;
using dnSpy.Shared.MVVM;
using dnSpy.Shared.Themes;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit.Search;

namespace dnSpy.TextEditor {
	[ExportAutoLoaded(LoadType = AutoLoadedLoadType.BeforePlugins)]
	sealed class DnSpyTextEditorThemeInitializer : IAutoLoaded {
		readonly IThemeManager themeManager;

		[ImportingConstructor]
		DnSpyTextEditorThemeInitializer(IThemeManager themeManager) {
			this.themeManager = themeManager;
			this.themeManager.ThemeChanged += ThemeManager_ThemeChanged;
			InitializeResources();
		}

		void ThemeManager_ThemeChanged(object sender, ThemeChangedEventArgs e) => InitializeResources();

		void InitializeResources() {
			var theme = themeManager.Theme;

			var specialBox = theme.GetTextColor(ColorType.SpecialCharacterBox);
			SpecialCharacterTextRunOptions.BackgroundBrush = specialBox.Background;
			SpecialCharacterTextRunOptions.ForegroundBrush = specialBox.Foreground;

			foreach (var f in typeof(TextTokenKind).GetFields()) {
				if (!f.IsLiteral)
					continue;
				var val = (TextTokenKind)f.GetValue(null);
				if (val != TextTokenKind.Last)
					UpdateTextEditorResource(val, f.Name);
			}

			UpdateDefaultHighlighter();
		}

		void UpdateTextEditorResource(TextTokenKind colorType, string name) {
			var theme = themeManager.Theme;

			var color = theme.GetTextColor(colorType.ToColorType());
			Application.Current.Resources[GetTextInheritedForegroundResourceKey(name)] = GetBrush(color.Foreground);
			Application.Current.Resources[GetTextInheritedBackgroundResourceKey(name)] = GetBrush(color.Background);
			Application.Current.Resources[GetTextInheritedFontStyleResourceKey(name)] = color.FontStyle ?? FontStyles.Normal;
			Application.Current.Resources[GetTextInheritedFontWeightResourceKey(name)] = color.FontWeight ?? FontWeights.Normal;

			color = theme.GetColor(colorType.ToColorType());
			Application.Current.Resources[GetInheritedForegroundResourceKey(name)] = GetBrush(color.Foreground);
			Application.Current.Resources[GetInheritedBackgroundResourceKey(name)] = GetBrush(color.Background);
			Application.Current.Resources[GetInheritedFontStyleResourceKey(name)] = color.FontStyle ?? FontStyles.Normal;
			Application.Current.Resources[GetInheritedFontWeightResourceKey(name)] = color.FontWeight ?? FontWeights.Normal;
		}

		static Brush GetBrush(Brush b) => b ?? Brushes.Transparent;
		static string GetTextInheritedForegroundResourceKey(string name) => string.Format("TETextInherited{0}Foreground", name);
		static string GetTextInheritedBackgroundResourceKey(string name) => string.Format("TETextInherited{0}Background", name);
		static string GetTextInheritedFontStyleResourceKey(string name) => string.Format("TETextInherited{0}FontStyle", name);
		static string GetTextInheritedFontWeightResourceKey(string name) => string.Format("TETextInherited{0}FontWeight", name);
		static string GetInheritedForegroundResourceKey(string name) => string.Format("TEInherited{0}Foreground", name);
		static string GetInheritedBackgroundResourceKey(string name) => string.Format("TEInherited{0}Background", name);
		static string GetInheritedFontStyleResourceKey(string name) => string.Format("TEInherited{0}FontStyle", name);
		static string GetInheritedFontWeightResourceKey(string name) => string.Format("TEInherited{0}FontWeight", name);

		static readonly Tuple<string, Dictionary<string, ColorType>>[] langFixes = new Tuple<string, Dictionary<string, ColorType>>[] {
			new Tuple<string, Dictionary<string, ColorType>>("XML",
				new Dictionary<string, ColorType>(StringComparer.Ordinal) {
					{ "Comment", ColorType.XmlComment },
					{ "CData", ColorType.XmlCDataSection },
					{ "DocType", ColorType.XmlName },
					{ "XmlDeclaration", ColorType.XmlName },
					{ "XmlTag", ColorType.XmlName },
					{ "AttributeName", ColorType.XmlAttributeName },
					{ "AttributeValue", ColorType.XmlAttributeValue },
					{ "Entity", ColorType.XmlAttributeName },
					{ "BrokenEntity", ColorType.XmlAttributeName },
				}
			)
		};
		void UpdateDefaultHighlighter() {
			foreach (var fix in langFixes)
				UpdateLanguage(fix.Item1, fix.Item2);
		}

		void UpdateLanguage(string name, Dictionary<string, ColorType> colorNames) {
			var lang = HighlightingManager.Instance.GetDefinition(name);
			Debug.Assert(lang != null);
			if (lang == null)
				return;

			foreach (var color in lang.NamedHighlightingColors) {
				ColorType colorType;
				bool b = colorNames.TryGetValue(color.Name, out colorType);
				Debug.Assert(b);
				if (!b)
					continue;
				var ourColor = themeManager.Theme.GetTextColor(colorType);
				color.Background = ourColor.Background.ToHighlightingBrush();
				color.Foreground = ourColor.Foreground.ToHighlightingBrush();
				color.FontWeight = ourColor.FontWeight;
				color.FontStyle = ourColor.FontStyle;
			}
		}
	}

	sealed class DnSpyTextEditor : ICSharpCode.AvalonEdit.TextEditor, IDisposable {
		static DnSpyTextEditor() {
			HighlightingManager.Instance.RegisterHighlighting(
				"IL", new string[] { ".il" }, () => {
					using (var s = typeof(DnSpyTextEditor).Assembly.GetManifestResourceStream(typeof(DnSpyTextEditor), "IL.xshd")) {
						using (var reader = new XmlTextReader(s))
							return HighlightingLoader.Load(reader, HighlightingManager.Instance);
					}
				}
			);
		}

		public IInputElement FocusedElement => this.TextArea;
		public DnSpyTextBuffer TextBuffer { get; }
		internal IThemeManager ThemeManager { get; }

		readonly ITextEditorSettings textEditorSettings;
		readonly SearchPanel searchPanel;

		public DnSpyTextEditor(IThemeManager themeManager, ITextEditorSettings textEditorSettings, ITextSnapshotColorizerCreator textBufferColorizerCreator, IContentTypeRegistryService contentTypeRegistryService) {
			this.ThemeManager = themeManager;
			this.textEditorSettings = textEditorSettings;
			this.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".il");
			this.textEditorSettings.PropertyChanged += TextEditorSettings_PropertyChanged;
			this.ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
			Options.AllowToggleOverstrikeMode = true;
			Options.RequireControlModifierForHyperlinkClick = false;
			this.TextBuffer = new DnSpyTextBuffer(this, textBufferColorizerCreator, contentTypeRegistryService.UnknownContentType);
			UpdateColors(false);

			searchPanel = SearchPanel.Install(TextArea);
			searchPanel.RegisterCommands(this.CommandBindings);
			searchPanel.Localization = new AvalonEditSearchPanelLocalization();

			TextArea.SelectionCornerRadius = 0;
			TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;
			TextArea.InputBindings.Add(new KeyBinding(new RelayCommand(a => PageUp()), Key.PageUp, ModifierKeys.Control));
			TextArea.InputBindings.Add(new KeyBinding(new RelayCommand(a => PageDown()), Key.PageDown, ModifierKeys.Control));
			TextArea.InputBindings.Add(new KeyBinding(new RelayCommand(a => UpDownLine(false)), Key.Down, ModifierKeys.Control));
			TextArea.InputBindings.Add(new KeyBinding(new RelayCommand(a => UpDownLine(true)), Key.Up, ModifierKeys.Control));
			this.AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus), true);
			this.AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostKeyboardFocus), true);

			TextArea.MouseRightButtonDown += (s, e) => GoToMousePosition();

			SetBinding(FontFamilyProperty, new Binding {
				Source = textEditorSettings,
				Path = new PropertyPath(nameof(textEditorSettings.FontFamily)),
				Mode = BindingMode.OneWay,
			});
			SetBinding(FontSizeProperty, new Binding {
				Source = textEditorSettings,
				Path = new PropertyPath(nameof(textEditorSettings.FontSize)),
				Mode = BindingMode.OneWay,
			});
			SetBinding(WordWrapProperty, new Binding {
				Source = textEditorSettings,
				Path = new PropertyPath(nameof(textEditorSettings.WordWrap)),
				Mode = BindingMode.OneWay,
			});

			OnHighlightCurrentLineChanged();
			OnShowLineNumbersChanged();
		}

		public void Dispose() {
			this.textEditorSettings.PropertyChanged -= TextEditorSettings_PropertyChanged;
			this.ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
			TextBuffer.Dispose();
		}

		protected override void OnDragOver(DragEventArgs e) {
			base.OnDragOver(e);

			if (!e.Handled) {
				// The text editor seems to allow anything
				if (e.Data.GetDataPresent(typeof(Tabs.TabItemImpl))) {
					e.Effects = DragDropEffects.None;
					e.Handled = true;
					return;
				}
			}
		}

		void TextEditorSettings_PropertyChanged(object sender, PropertyChangedEventArgs e) {
			if (e.PropertyName == nameof(textEditorSettings.HighlightCurrentLine))
				OnHighlightCurrentLineChanged();
			else if (e.PropertyName == nameof(textEditorSettings.ShowLineNumbers))
				OnShowLineNumbersChanged();
		}

		void OnHighlightCurrentLineChanged() => Options.HighlightCurrentLine = textEditorSettings.HighlightCurrentLine;
		public void OnShowLineNumbersChanged() => ShowLineMargin(textEditorSettings.ShowLineNumbers);

		public void ShowLineMargin(bool enable) {
			foreach (var margin in TextArea.LeftMargins) {
				if (margin is LineNumberMargin)
					margin.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
				else if (margin is System.Windows.Shapes.Line)
					margin.Visibility = Visibility.Collapsed;
			}
		}

		void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
			TextArea.Caret.Show();
			UpdateCurrentLineColors(true);
			OnHighlightCurrentLineChanged();
		}

		void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
			TextArea.Caret.Hide();
			UpdateCurrentLineColors(true);
			// Do as VS: don't hide the highlighted line
		}

		public void GoToMousePosition() {
			var pos = GetPositionFromMousePosition();
			if (pos != null) {
				TextArea.Caret.Position = pos.Value;
				TextArea.Caret.DesiredXPos = double.NaN;
			}
		}

		public TextViewPosition? GetPositionFromMousePosition() =>
			TextArea.TextView.GetPosition(Mouse.GetPosition(TextArea.TextView) + TextArea.TextView.ScrollOffset);

		public void SetCaretPosition(int line, int column, double desiredXPos = double.NaN) {
			TextArea.Caret.Location = new TextLocation(line, column);
			TextArea.Caret.DesiredXPos = desiredXPos;
		}

		static Point FilterCaretPos(TextView textView, Point pt) {
			Point firstPos;
			if (textView.VisualLines.Count == 0)
				firstPos = new Point(0, 0);
			else {
				var line = textView.VisualLines[0];
				if (line.VisualTop < textView.VerticalOffset && textView.VisualLines.Count > 1)
					line = textView.VisualLines[1];
				firstPos = line.GetVisualPosition(0, VisualYPosition.LineMiddle);
			}

			Point lastPos;
			if (textView.VisualLines.Count == 0)
				lastPos = new Point(0, 0);
			else {
				var line = textView.VisualLines[textView.VisualLines.Count - 1];
				if (line.VisualTop - textView.VerticalOffset + line.Height > textView.ActualHeight && textView.VisualLines.Count > 1)
					line = textView.VisualLines[textView.VisualLines.Count - 2];
				lastPos = line.GetVisualPosition(0, VisualYPosition.LineMiddle);
			}

			if (pt.Y < firstPos.Y)
				return new Point(pt.X, firstPos.Y);
			else if (pt.Y > lastPos.Y)
				return new Point(pt.X, lastPos.Y);
			return pt;
		}

		void TextArea_PreviewKeyDown(object sender, KeyEventArgs e) {
			if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.PageDown || e.Key == Key.PageUp)) {
				var textView = TextArea.TextView;
				var si = (System.Windows.Controls.Primitives.IScrollInfo)textView;

				// Re-use the existing code in AvalonEdit
				var cmd = e.Key == Key.PageDown ? EditingCommands.MoveDownByPage : EditingCommands.MoveUpByPage;
				var target = textView;
				bool canExec = cmd.CanExecute(null, target);
				Debug.Assert(canExec);
				if (canExec) {
					if (e.Key == Key.PageDown)
						si.PageDown();
					else
						si.PageUp();

					cmd.Execute(null, target);
					e.Handled = true;
				}
				return;
			}
		}

		new void PageUp() {
			var textView = TextArea.TextView;
			textView.EnsureVisualLines();
			if (textView.VisualLines.Count > 0) {
				var line = textView.VisualLines[0];
				// If the full height isn't visible, pick the next one
				if (line.VisualTop < textView.VerticalOffset && textView.VisualLines.Count > 1)
					line = textView.VisualLines[1];
				var docLine = line.FirstDocumentLine;
				var caret = TextArea.Caret;
				SetCaretPosition(docLine.LineNumber, caret.Location.Column);
			}
		}

		new void PageDown() {
			var textView = TextArea.TextView;
			textView.EnsureVisualLines();
			if (textView.VisualLines.Count > 0) {
				var line = textView.VisualLines[textView.VisualLines.Count - 1];
				// If the full height isn't visible, pick the previous one
				if (line.VisualTop - textView.VerticalOffset + line.Height > textView.ActualHeight && textView.VisualLines.Count > 1)
					line = textView.VisualLines[textView.VisualLines.Count - 2];
				var docLine = line.LastDocumentLine;
				var caret = TextArea.Caret;
				SetCaretPosition(docLine.LineNumber, caret.Location.Column);
			}
		}

		void UpDownLine(bool up) {
			var textView = TextArea.TextView;
			var scrollViewer = ((System.Windows.Controls.Primitives.IScrollInfo)textView).ScrollOwner;
			textView.EnsureVisualLines();

			var currPos = FilterCaretPos(textView, textView.GetVisualPosition(TextArea.Caret.Position, VisualYPosition.LineMiddle));

			if (!up)
				scrollViewer.LineDown();
			else
				scrollViewer.LineUp();
			textView.UpdateLayout();
			textView.EnsureVisualLines();

			var newPos = FilterCaretPos(textView, currPos);
			var newVisPos = textView.GetPosition(newPos);
			Debug.Assert(newVisPos != null);
			if (newVisPos != null)
				TextArea.Caret.Position = newVisPos.Value;
		}

		void ThemeManager_ThemeChanged(object sender, ThemeChangedEventArgs e) {
			var theme = ThemeManager.Theme;
			var marker = theme.GetColor(ColorType.SearchResultMarker);
			searchPanel.MarkerBrush = marker.Background ?? Brushes.LightGreen;
			UpdateColors(true);
		}

		void UpdateColors(bool redraw = true) {
			var theme = ThemeManager.Theme;
			var textColor = theme.GetColor(ColorType.Text);
			Background = textColor.Background;
			Foreground = textColor.Foreground;
			FontWeight = textColor.FontWeight ?? FontWeights.Regular;
			FontStyle = textColor.FontStyle ?? FontStyles.Normal;

			LineNumbersForeground = theme.GetColor(ColorType.LineNumber).Foreground;

			var linkColor = theme.GetTextColor(ColorType.Link);
			TextArea.TextView.LinkTextForegroundBrush = (linkColor.Foreground ?? textColor.Foreground);
			TextArea.TextView.LinkTextBackgroundBrush = linkColor.Background ?? Brushes.Transparent;

			var sel = theme.GetColor(ColorType.Selection);
			TextArea.SelectionBorder = null;
			TextArea.SelectionBrush = sel.Background;
			TextArea.SelectionForeground = sel.Foreground;

			UpdateCurrentLineColors(false);

			if (redraw)
				TextArea.TextView.Redraw();
		}

		void UpdateCurrentLineColors(bool redraw) {
			var theme = ThemeManager.Theme;
			bool hasFocus = this.IsKeyboardFocusWithin;
			var currentLine = theme.GetColor(hasFocus ? ColorType.CurrentLine : ColorType.CurrentLineNoFocus);
			TextArea.TextView.CurrentLineBackground = currentLine.Background;
			TextArea.TextView.CurrentLineBorder = new Pen(currentLine.Foreground, 2);
			if (redraw)
				TextArea.TextView.Redraw();
		}

		protected override IVisualLineTransformer CreateColorizer(IHighlightingDefinition highlightingDefinition) {
			if (highlightingDefinition.Name == "C#" || highlightingDefinition.Name == "VB" ||
				highlightingDefinition.Name == "IL")
				return new NewHighlightingColorizer(this);
			return base.CreateColorizer(highlightingDefinition);
		}

		sealed class NewHighlightingColorizer : HighlightingColorizer {
			readonly DnSpyTextEditor textEditor;

			public NewHighlightingColorizer(DnSpyTextEditor textEditor) {
				this.textEditor = textEditor;
			}

			protected override IHighlighter CreateHighlighter(TextView textView, TextDocument document) =>
				new DnSpyTextEditorHighlighter(textEditor, document);
		}
	}
}
