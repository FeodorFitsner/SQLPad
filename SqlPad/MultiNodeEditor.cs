﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using SqlPad.Commands;

namespace SqlPad
{
	public class MultiNodeEditor
	{
		private readonly TextEditor _editor;

		private readonly TextAnchor _masterAnchorStart;
		private readonly TextAnchor _masterAnchorEnd;
		private readonly List<TextAnchor> _anchors = new List<TextAnchor>();

		private bool IsModificationValid => _masterAnchorStart.Offset <= _editor.CaretOffset && _masterAnchorEnd.Offset >= _editor.CaretOffset;

		private MultiNodeEditor(TextEditor editor, MultiNodeEditorData data)
		{
			_editor = editor;

			_masterAnchorStart = editor.Document.CreateAnchor(data.CurrentNode.SourcePosition.IndexStart);
			_masterAnchorEnd = editor.Document.CreateAnchor(data.CurrentNode.SourcePosition.IndexEnd + 1);

			foreach (var segment in data.SynchronizedSegments)
			{
				_anchors.Add(editor.Document.CreateAnchor(segment.IndexStart));
			}
		}

		public bool Replace(string newText)
		{
			if (!IsModificationValid)
			{
				return false;
			}

			var editTerminalOffset = _editor.SelectionStart - _masterAnchorStart.Offset;

			foreach (var anchor in _anchors)
			{
				_editor.Document.Replace(anchor.Offset + editTerminalOffset, _editor.SelectionLength, newText);
			}

			return true;
		}

		public bool RemoveCharacter(bool reverse)
		{
			if (!IsModificationValid)
			{
				return false;
			}

			var editTerminalOffset = _editor.SelectionStart - _masterAnchorStart.Offset;
			var selectionCharacter = reverse && _editor.SelectionLength == 0 ? 1 : 0;
			var removedCharacters = _editor.SelectionLength == 0 ? 1 : _editor.SelectionLength;
			foreach (var anchor in _anchors)
			{
				_editor.Document.Remove(anchor.Offset + editTerminalOffset - selectionCharacter, removedCharacters);
			}

			return true;
		}

		public static bool TryCreateMultiNodeEditor(TextEditor editor, ActionExecutionContext executionContext, IMultiNodeEditorDataProvider dataProvider, out MultiNodeEditor multiNodeEditor)
		{
			multiNodeEditor = null;
			if (!String.Equals(editor.Text, executionContext.DocumentRepository.StatementText))
			{
				return false;
			}

			var data = dataProvider.GetMultiNodeEditorData(executionContext);
			if (data.CurrentNode == null)
			{
				return false;
			}

			multiNodeEditor = new MultiNodeEditor(editor, data);
			return true;
		}

		public SourcePosition MasterSegment => SourcePosition.Create(_masterAnchorStart.Offset, _masterAnchorEnd.Offset);

		public IReadOnlyCollection<SourcePosition> SynchronizedSegments
		{
			get
			{
				var segment = MasterSegment;
				var segments = new List<SourcePosition>(_anchors.Count);

				foreach (var anchor in _anchors)
				{
					segments.Add(SourcePosition.Create(anchor.Offset, anchor.Offset + segment.Length - 1));
				}

				return segments;
			}
		}
	}

	public struct MultiNodeEditorData
	{
		public StatementGrammarNode CurrentNode { get; set; }

		public IReadOnlyCollection<SourcePosition> SynchronizedSegments { get; set; }
	}

	public class SqlEditorBackgroundRenderer : IBackgroundRenderer
	{
		private static readonly Pen NullPen = new Pen(Brushes.Transparent, 0);
		private static readonly Pen MasterEdgePen = new Pen(Brushes.Red, 1);
		private static readonly Pen SynchronizedEdgePen = new Pen(Brushes.Black, 1) { DashStyle = DashStyles.Dot };

		private static readonly SolidColorBrush HighlightUsageBrush = Brushes.Turquoise;
		private static readonly SolidColorBrush HighlightDefinitionBrush = Brushes.SandyBrown;

		private readonly Stack<IReadOnlyCollection<HighlightSegment>> _highlightSegments = new Stack<IReadOnlyCollection<HighlightSegment>>();
		private readonly TextEditor _textEditor;

		public KnownLayer Layer { get; } = KnownLayer.Background;

		public IEnumerable<TextSegment> HighlightSegments => _highlightSegments.SelectMany(g => g.Select(s => s.Segment));

		public IReadOnlyCollection<SourcePosition> SynchronizedSegments { get; set; }

		public SourcePosition? MasterSegment { get; set; }

		static SqlEditorBackgroundRenderer()
		{
			SynchronizedEdgePen.Freeze();
			MasterEdgePen.Freeze();
			NullPen.Freeze();
		}

		public SqlEditorBackgroundRenderer(TextEditor textEditor)
		{
			_textEditor = textEditor;
		}

		public void Draw(TextView textView, DrawingContext drawingContext)
		{
			if (SynchronizedSegments != null)
			{
				if (MasterSegment.HasValue)
				{
					DrawRectangle(textView, drawingContext, MasterSegment.Value, Brushes.Transparent, MasterEdgePen);
				}

				foreach (var segment in SynchronizedSegments)
				{
					DrawRectangle(textView, drawingContext, segment, Brushes.Transparent, SynchronizedEdgePen);
				}
			}

			foreach (var highlightSegmentGroup in _highlightSegments)
			{
				foreach (var highlightSegment in highlightSegmentGroup)
				{
					var brush = highlightSegment.Segment.DisplayOptions == DisplayOptions.Definition ? HighlightDefinitionBrush : HighlightUsageBrush;
					var indexStart = highlightSegment.HighlightStartAnchor.Offset;
					DrawRectangle(textView, drawingContext, SourcePosition.Create(indexStart, indexStart + highlightSegment.Segment.Length), brush, NullPen);
				}
			}
		}

		private static void DrawRectangle(TextView textView, DrawingContext drawingContext, SourcePosition sourceSegment, Brush brush, Pen pen)
		{
			var segment = new ICSharpCode.AvalonEdit.Document.TextSegment { StartOffset = sourceSegment.IndexStart, EndOffset = sourceSegment.IndexEnd };
			foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
			{
				drawingContext.DrawRectangle(brush, pen, rectangle);
			}
		}

		public void AddHighlightSegments(ICollection<TextSegment> highlightSegments)
		{
			if (highlightSegments != null)
			{
				if (_highlightSegments.Any(c => c.Any(s => s.Segment.Equals(highlightSegments.First()))))
				{
					return;
				}

				var anchoredSegment = highlightSegments.Select(s => new HighlightSegment { Segment = s, HighlightStartAnchor = _textEditor.Document.CreateAnchor(s.IndextStart) }).ToArray();
				_highlightSegments.Push(anchoredSegment);
			}
			else if (_highlightSegments.Count > 0)
			{
				_highlightSegments.Pop();
			}
		}

		private struct HighlightSegment
		{
			public TextAnchor HighlightStartAnchor;

			public TextSegment Segment;
		}
	}
}