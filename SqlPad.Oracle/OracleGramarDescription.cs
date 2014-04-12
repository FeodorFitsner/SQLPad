﻿using System;
using System.Collections.Generic;

namespace SqlPad.Oracle
{
	/// <summary>
	/// This class provides token constants for Oracle SQL grammar.
	/// </summary>
	public static class OracleGrammarDescription
	{
		/// <summary>
		/// This class provides the non-terminal constants.
		/// </summary>
		public static class NonTerminals
		{
			public const string AliasedExpression = "AliasedExpression";
			public const string AliasedExpressionListOrAliasedGroupingExpressionList = "AliasedExpressionListOrAliasedGroupingExpressionList";
			public const string AliasedExpressionListOrAliasedGroupingExpressionListChained = "AliasedExpressionListOrAliasedGroupingExpressionListChained";
			public const string AliasedExpressionOrAllTableColumns = "AliasedExpressionOrAllTableColumns";
			public const string AnalyticClause = "AnalyticClause";
			public const string AnyChainedList = "AnyChainedList";
			public const string BetweenFollowingClause = "BetweenFollowingClause";
			public const string BetweenPrecedingAndFollowingClause = "BetweenPrecedingAndFollowingClause";
			public const string BetweenPrecedingClause = "BetweenPrecedingClause";
			public const string BetweenSystemChangeNumberOrTimestamp = "BetweenSystemChangeNumberOrTimestamp";
			public const string BetweenSystemChangeNumberOrTimestampOrPeriodForBetween = "BetweenSystemChangeNumberOrTimestampOrPeriodForBetween";
			public const string BindVariableExpression = "BindVariableExpression";
			public const string BindVariableExpressionCommaChainedList = "BindVariableExpressionCommaChainedList";
			public const string CaseExpression = "CaseExpression";
			public const string CaseExpressionElseBranch = "CaseExpressionElseBranch";
			public const string ChainedCondition = "ChainedCondition";
			public const string ColumnAlias = "ColumnAlias";
			public const string ColumnIdentifierChainedList = "ColumnIdentifierChainedList";
			public const string ColumnIdentifierList = "ColumnIdentifierList";
			public const string ColumnReference = "ColumnReference";
			public const string CommitComment = "CommitComment";
			public const string CommitCommentOrWriteOrForce = "CommitCommentOrWriteOrForce";
			public const string CommitSetScn = "CommitSetScn";
			public const string CommitWriteClause = "CommitWriteClause";
			public const string ConcatenatedSubquery = "ConcatenatedSubquery";
			public const string Condition = "Condition";
			public const string ConstraintName = "ConstraintName";
			public const string CrossOrOuter = "CrossOrOuter";
			public const string CrossOrOuterApplyClause = "CrossOrOuterApplyClause";
			public const string DatabaseLink = "DatabaseLink";
			public const string DataType = "DataType";
			public const string DataTypePrecisionAndScale = "DataTypePrecisionAndScale";
			public const string DepthOrBreadth = "DepthOrBreadth";
			public const string DistinctModifier = "DistinctModifier";
			public const string EscapeClause = "EscapeClause";
			public const string Expression = "Expression";
			public const string ExpressionCommaChainedList = "ExpressionCommaChainedList";
			public const string ExpressionList = "ExpressionList";
			public const string ExpressionListOrNestedQuery = "ExpressionListOrNestedQuery";
			public const string ExpressionMathOperatorChainedList = "ExpressionMathOperatorChainedList";
			public const string ExpressionOrMultiset = "ExpressionOrMultiset";
			public const string FirstOrLast = "FirstOrLast";
			public const string FirstOrNext = "FirstOrNext";
			public const string FlashbackAsOfClause = "FlashbackAsOfClause";
			public const string FlashbackMaximumValue = "FlashbackMaximumValue";
			public const string FlashbackMinimumValue = "FlashbackMinimumValue";
			public const string FlashbackPeriodFor = "FlashbackPeriodFor";
			public const string FlashbackQueryClause = "FlashbackQueryClause";
			public const string FlashbackVersionsClause = "FlashbackVersionsClause";
			public const string ForUpdateClause = "ForUpdateClause";
			public const string ForUpdateColumn = "ForUpdateColumn";
			public const string ForUpdateColumnChained = "ForUpdateColumnChained";
			public const string ForUpdateLockingClause = "ForUpdateLockingClause";
			public const string ForUpdateOfColumnsClause = "ForUpdateOfColumnsClause";
			public const string ForUpdateWaitClause = "ForUpdateWaitClause";
			public const string FromClause = "FromClause";
			public const string FromClauseChained = "FromClauseChained";
			public const string FunctionParameters = "FunctionParameters";
			public const string GroupByClause = "GroupByClause";
			public const string GroupingClause = "GroupingClause";
			public const string GroupingClauseChained = "GroupingClauseChained";
			public const string GroupingExpressionList = "GroupingExpressionList";
			public const string GroupingExpressionListChained = "GroupingExpressionListChained";
			public const string GroupingExpressionListOrNestedQuery = "GroupingExpressionListOrNestedQuery";
			public const string GroupingExpressionListOrRollupCubeClause = "GroupingExpressionListOrRollupCubeClause";
			public const string GroupingExpressionListOrRollupCubeClauseChained = "GroupingExpressionListOrRollupCubeClauseChained";
			public const string GroupingExpressionListWithMandatoryExpressions = "GroupingExpressionListWithMandatoryExpressions";
			public const string GroupingExpressionListWithMandatoryExpressionsChained = "GroupingExpressionListWithMandatoryExpressionsChained";
			public const string GroupingSetsClause = "GroupingSetsClause";
			public const string HavingClause = "HavingClause";
			public const string HierarchicalQueryClause = "HierarchicalQueryClause";
			public const string HierarchicalQueryConnectByClause = "HierarchicalQueryConnectByClause";
			public const string HierarchicalQueryStartWithClause = "HierarchicalQueryStartWithClause";
			public const string IdentifierOrParenthesisEnclosedColumnIdentifierList = "IdentifierOrParenthesisEnclosedColumnIdentifierList";
			public const string ImmediateOrBatch = "ImmediateOrBatch";
			public const string IncludeOrExclude = "IncludeOrExclude";
			public const string InnerJoinClause = "InnerJoinClause";
			public const string InnerTableReference = "InnerTableReference";
			public const string IntegerOrAsterisk = "IntegerOrAsterisk";
			public const string JoinClause = "JoinClause";
			public const string JoinColumnsOrCondition = "JoinColumnsOrCondition";
			public const string LikeOperator = "LikeOperator";
			public const string LogicalOperator = "LogicalOperator";
			public const string MathOperator = "MathOperator";
			public const string NaturalOrOuterJoinType = "NaturalOrOuterJoinType";
			public const string NestedQuery = "NestedQuery";
			public const string NullNaNOrInfinite = "NullNaNOrInfinite";
			public const string NullsClause = "NullsClause";
			public const string ObjectPrefix = "ObjectPrefix";
			public const string OnlyOrWithTies = "OnlyOrWithTies";
			public const string OrderByClause = "OrderByClause";
			public const string OrderExpression = "OrderExpression";
			public const string OrderExpressionChained = "OrderExpressionChained";
			public const string OrderExpressionType = "OrderExpressionType";
			public const string OuterJoinClause = "OuterJoinClause";
			public const string OuterJoinOld = "OuterJoinOld";
			public const string OuterJoinPartitionClause = "OuterJoinPartitionClause";
			public const string OuterJoinType = "OuterJoinType";
			public const string OuterJoinTypeWithKeyword = "OuterJoinTypeWithKeyword";
			public const string OverQueryPartitionClause = "OverQueryPartitionClause";
			public const string ParenthesisEnclosedAggregationFunctionParameters = "ParenthesisEnclosedAggregationFunctionParameters";
			public const string ParenthesisEnclosedColumnIdentifierList = "ParenthesisEnclosedColumnIdentifierList";
			public const string ParenthesisEnclosedExpressionList = "ParenthesisEnclosedExpressionList";
			public const string ParenthesisEnclosedExpressionListWithMandatoryExpressions = "ParenthesisEnclosedExpressionListWithMandatoryExpressions";
			public const string ParenthesisEnclosedFunctionParameters = "ParenthesisEnclosedFunctionParameters";
			public const string ParenthesisEnclosedGroupingExpressionList = "ParenthesisEnclosedGroupingExpressionList";
			public const string ParenthesisEnclosedStringOrIntegerLiteralList = "ParenthesisEnclosedStringOrIntegerLiteralList";
			public const string PartitionExtensionClause = "PartitionExtensionClause";
			public const string PartitionNameOrKeySet = "PartitionNameOrKeySet";
			public const string PartitionOrDatabaseLink = "PartitionOrDatabaseLink";
			public const string PivotAliasedAggregationFunctionList = "PivotAliasedAggregationFunctionList";
			public const string PivotAliasedAggregationFunctionListChained = "PivotAliasedAggregationFunctionListChained";
			public const string PivotClause = "PivotClause";
			public const string PivotClauseOrUnpivotClauseOrRowPatternClause = "PivotClauseOrUnpivotClauseOrRowPatternClause";
			public const string PivotExpressionsOrAnyListOrNestedQuery = "PivotExpressionsOrAnyListOrNestedQuery";
			public const string PivotForClause = "PivotForClause";
			public const string PivotInClause = "PivotInClause";
			public const string PrecedingOnlyClause = "PrecedingOnlyClause";
			public const string PrecedingOnlyOrBetweenPrecedingAndFollowing = "PrecedingOnlyOrBetweenPrecedingAndFollowing";
			public const string PrecedingOrFollowing = "PrecedingOrFollowing";
			public const string Prefix = "Prefix";
			public const string PrefixedColumnReference = "PrefixedColumnReference";
			public const string QueryBlock = "QueryBlock";
			public const string QueryPartitionClause = "QueryPartitionClause";
			public const string QueryTableExpression = "QueryTableExpression";
			public const string ReadOnlyOrCheckOption = "ReadOnlyOrCheckOption";
			public const string RelationalEquiOperator = "RelationalEquiOperator";
			public const string RelationalNonEquiOperator = "RelationalNonEquiOperator";
			public const string RelationalOperator = "RelationalOperator";
			public const string ReturnOrReturning = "ReturnOrReturning";
			public const string RollupCubeClause = "RollupCubeClause";
			public const string RowLimitingClause = "RowLimitingClause";
			public const string RowLimitingFetchClause = "RowLimitingFetchClause";
			public const string RowLimitingOffsetClause = "RowLimitingOffsetClause";
			public const string RowOrRows = "RowOrRows";
			public const string RowsOrRange = "RowsOrRange";
			public const string SampleClause = "SampleClause";
			public const string Scale = "Scale";
			public const string SchemaPrefix = "SchemaPrefix";
			public const string SearchedCaseExpressionBranch = "SearchedCaseExpressionBranch";
			public const string SeedClause = "SeedClause";
			public const string SelectExpressionExpressionChainedList = "SelectExpressionExpressionChainedList";
			public const string SelectList = "SelectList";
			public const string SerializableOrReadCommitted = "SerializableOrReadCommitted";
			public const string SetOperation = "SetOperation";
			public const string SetQualifier = "SetQualifier";
			public const string SimpleCaseExpressionBranch = "SimpleCaseExpressionBranch";
			public const string SortOrder = "SortOrder";
			public const string StringOrIntegerLiteral = "StringOrIntegerLiteral";
			public const string StringOrIntegerLiteralList = "StringOrIntegerLiteralList";
			public const string StringOrIntegerLiteralListChained = "StringOrIntegerLiteralListChained";
			public const string StringOrIntegerLiteralOrParenthesisEnclosedStringOrIntegerLiteralList = "StringOrIntegerLiteralOrParenthesisEnclosedStringOrIntegerLiteralList";
			public const string Subquery = "Subquery";
			public const string SubqueryComponent = "SubqueryComponent";
			public const string SubqueryComponentChained = "SubqueryComponentChained";
			public const string SubqueryFactoringClause = "SubqueryFactoringClause";
			public const string SubqueryFactoringCycleClause = "SubqueryFactoringCycleClause";
			public const string SubqueryFactoringSearchClause = "SubqueryFactoringSearchClause";
			public const string SubQueryRestrictionClause = "SubQueryRestrictionClause";
			public const string SystemChangeNumberOrTimestamp = "SystemChangeNumberOrTimestamp";
			public const string SystemChangeNumberOrTimestampOrPeriodFor = "SystemChangeNumberOrTimestampOrPeriodFor";
			public const string TableCollectionExpression = "TableCollectionExpression";
			public const string TableReference = "TableReference";
			public const string TransactionModeOrIsolationLevelOrRollbackSegment = "TransactionModeOrIsolationLevelOrRollbackSegment";
			public const string TransactionNameClause = "TransactionNameClause";
			public const string TransactionReadOnlyOrReadWrite = "TransactionReadOnlyOrReadWrite";
			public const string UnpivotClause = "UnpivotClause";
			public const string UnpivotInClause = "UnpivotInClause";
			public const string UnpivotNullsClause = "UnpivotNullsClause";
			public const string UnpivotValueSelector = "UnpivotValueSelector";
			public const string UnpivotValueToColumnTransformationList = "UnpivotValueToColumnTransformationList";
			public const string UnpivotValueToColumnTransformationListChained = "UnpivotValueToColumnTransformationListChained";
			public const string WaitOrNowait = "WaitOrNowait";
			public const string WhereClause = "WhereClause";
			public const string WindowingClause = "WindowingClause";
		}

		/// <summary>
		/// This class provides the terminal constants.
		/// </summary>
		public static class Terminals
		{
			public const string Alias = "Alias";
			public const string All = "All";
			public const string And = "And";
			public const string Any = "Any";
			public const string Apply = "Apply";
			public const string As = "As";
			public const string Asc = "Asc";
			public const string Asterisk = "Asterisk";
			public const string At = "At";
			public const string Batch = "Batch";
			public const string Between = "Between";
			public const string BindVariableIdentifier = "BindVariableIdentifier";
			public const string Block = "Block";
			public const string Breadth = "Breadth";
			public const string By = "By";
			public const string Case = "Case";
			public const string Cast = "Cast";
			public const string Check = "Check";
			public const string Colon = "Colon";
			public const string Column = "Column";
			public const string Comma = "Comma";
			public const string Comment = "Comment";
			public const string Commit = "Commit";
			public const string Committed = "Committed";
			public const string Connect = "Connect";
			public const string Constraint = "Constraint";
			public const string Count = "Count";
			public const string Cross = "Cross";
			public const string Cube = "Cube";
			public const string Current = "Current";
			public const string Cycle = "Cycle";
			public const string DatabaseLinkIdentifier = "DatabaseLinkIdentifier";
			public const string DataTypeName = "DataTypeName";
			public const string Date = "Date";
			public const string Default = "Default";
			public const string Delete = "Delete";
			public const string DenseRank = "DenseRank";
			public const string Depth = "Depth";
			public const string Desc = "Desc";
			public const string Distinct = "Distinct";
			public const string Dot = "Dot";
			public const string Else = "Else";
			public const string End = "End";
			public const string Errors = "Errors";
			public const string Escape = "Escape";
			public const string Exclude = "Exclude";
			public const string Exists = "Exists";
			public const string Fetch = "Fetch";
			public const string First = "First";
			public const string Following = "Following";
			public const string For = "For";
			public const string Force = "Force";
			public const string From = "From";
			public const string Full = "Full";
			public const string Group = "Group";
			public const string Grouping = "Grouping";
			public const string Having = "Having";
			public const string Identifier = "Identifier";
			public const string Ignore = "Ignore";
			public const string Immediate = "Immediate";
			public const string In = "In";
			public const string Include = "Include";
			public const string Inner = "Inner";
			public const string IntegerLiteral = "IntegerLiteral";
			public const string Intersect = "Intersect";
			public const string Into = "Into";
			public const string Is = "Is";
			public const string Isolation = "Isolation";
			public const string Join = "Join";
			public const string Keep = "Keep";
			public const string Last = "Last";
			public const string Lateral = "Lateral";
			public const string Left = "Left";
			public const string LeftParenthesis = "LeftParenthesis";
			public const string Level = "Level";
			public const string Like = "Like";
			public const string LikeUcs2 = "LikeUcs2";
			public const string LikeUcs4 = "LikeUcs4";
			public const string LikeUnicode = "LikeUnicode";
			public const string Limit = "Limit";
			public const string Locked = "Locked";
			public const string Log = "Log";
			public const string MathDivide = "MathDivide";
			public const string MathEquals = "MathEquals";
			public const string MathFactor = "MathFactor";
			public const string MathGreatherThan = "MathGreatherThan";
			public const string MathGreatherThanOrEquals = "MathGreatherThanOrEquals";
			public const string MathInfinite = "MathInfinite";
			public const string MathLessThan = "MathLessThan";
			public const string MathLessThanOrEquals = "MathLessThanOrEquals";
			public const string MathMinus = "MathMinus";
			public const string MathNotANumber = "MathNotANumber";
			public const string MathNotEqualsC = "MathNotEqualsC";
			public const string MathNotEqualsCircumflex = "MathNotEqualsCircumflex";
			public const string MathNotEqualsSql = "MathNotEqualsSql";
			public const string MathPlus = "MathPlus";
			public const string MaximumValue = "MaximumValue";
			public const string MinimumValue = "MinimumValue";
			public const string Model = "Model";
			public const string Multiset = "Multiset";
			public const string Name = "Name";
			public const string Natural = "Natural";
			public const string Next = "Next";
			public const string NoCycle = "NoCycle";
			public const string Not = "Not";
			public const string Nowait = "Nowait";
			public const string Null = "Null";
			public const string Nulls = "Nulls";
			public const string NumberLiteral = "NumberLiteral";
			public const string ObjectIdentifier = "ObjectIdentifier";
			public const string Of = "Of";
			public const string Offset = "Offset";
			public const string On = "On";
			public const string Only = "Only";
			public const string OperatorConcatenation = "OperatorConcatenation";
			public const string Option = "Option";
			public const string Or = "Or";
			public const string Order = "Order";
			public const string Outer = "Outer";
			public const string Over = "Over";
			public const string Partition = "Partition";
			public const string Percent = "Percent";
			public const string Period = "Period";
			public const string Pivot = "Pivot";
			public const string Preceding = "Preceding";
			public const string Public = "Public";
			public const string Range = "Range";
			public const string Read = "Read";
			public const string Ref = "Ref";
			public const string Reject = "Reject";
			public const string Return = "Return";
			public const string Returning = "Returning";
			public const string Right = "Right";
			public const string RightParenthesis = "RightParenthesis";
			public const string Rollback = "Rollback";
			public const string Rollup = "Rollup";
			public const string Row = "Row";
			public const string RowIdPseudoColumn = "RowIdPseudoColumn";
			public const string RowNumberPseudoColumn = "RowNumberPseudoColumn";
			public const string Rows = "Rows";
			public const string Sample = "Sample";
			public const string SchemaIdentifier = "SchemaIdentifier";
			public const string Search = "Search";
			public const string Seed = "Seed";
			public const string Segment = "Segment";
			public const string Select = "Select";
			public const string Semicolon = "Semicolon";
			public const string SequenceCurrentValue = "SequenceCurrentValue";
			public const string SequenceNextValue = "SequenceNextValue";
			public const string Serializable = "Serializable";
			public const string Set = "Set";
			public const string SetMinus = "SetMinus";
			public const string Sets = "Sets";
			public const string Siblings = "Siblings";
			public const string Skip = "Skip";
			public const string Some = "Some";
			public const string Space = "Space";
			public const string Start = "Start";
			public const string StringLiteral = "StringLiteral";
			public const string Subpartition = "Subpartition";
			public const string SystemChangeNumber = "SystemChangeNumber";
			public const string Table = "Table";
			public const string Then = "Then";
			public const string Ties = "Ties";
			public const string Timestamp = "Timestamp";
			public const string To = "To";
			public const string Transaction = "Transaction";
			public const string Treat = "Treat";
			public const string Unbounded = "Unbounded";
			public const string Union = "Union";
			public const string Unique = "Unique";
			public const string Unlimited = "Unlimited";
			public const string Unpivot = "Unpivot";
			public const string Update = "Update";
			public const string Use = "Use";
			public const string Using = "Using";
			public const string Versions = "Versions";
			public const string Wait = "Wait";
			public const string When = "When";
			public const string Where = "Where";
			public const string With = "With";
			public const string Work = "Work";
			public const string Write = "Write";
			public const string Xml = "Xml";
			
			private static readonly HashSet<string> AllTerminalsInternal = new HashSet<string> { Alias, All, And, Any, Apply, As, Asc, Asterisk, At, Batch, Between, BindVariableIdentifier, Block, Breadth, By, Case, Cast, Check, Colon, Column, Comma, Comment, Commit, Committed, Connect, Constraint, Count, Cross, Cube, Current, Cycle, DatabaseLinkIdentifier, DataTypeName, Date, Default, Delete, DenseRank, Depth, Desc, Distinct, Dot, Else, End, Errors, Escape, Exclude, Exists, Fetch, First, Following, For, Force, From, Full, Group, Grouping, Having, Identifier, Ignore, Immediate, In, Include, Inner, IntegerLiteral, Intersect, Into, Is, Isolation, Join, Keep, Last, Lateral, Left, LeftParenthesis, Level, Like, LikeUcs2, LikeUcs4, LikeUnicode, Limit, Locked, Log, MathDivide, MathEquals, MathFactor, MathGreatherThan, MathGreatherThanOrEquals, MathInfinite, MathLessThan, MathLessThanOrEquals, MathMinus, MathNotANumber, MathNotEqualsC, MathNotEqualsCircumflex, MathNotEqualsSql, MathPlus, MaximumValue, MinimumValue, Model, Multiset, Name, Natural, Next, NoCycle, Not, Nowait, Null, Nulls, NumberLiteral, ObjectIdentifier, Of, Offset, On, Only, OperatorConcatenation, Option, Or, Order, Outer, Over, Partition, Percent, Period, Pivot, Preceding, Public, Range, Read, Ref, Reject, Return, Returning, Right, RightParenthesis, Rollback, Rollup, Row, RowIdPseudoColumn, RowNumberPseudoColumn, Rows, Sample, SchemaIdentifier, Search, Seed, Segment, Select, Semicolon, SequenceCurrentValue, SequenceNextValue, Serializable, Set, SetMinus, Sets, Siblings, Skip, Some, Space, Start, StringLiteral, Subpartition, SystemChangeNumber, Table, Then, Ties, Timestamp, To, Transaction, Treat, Unbounded, Union, Unique, Unlimited, Unpivot, Update, Use, Using, Versions, Wait, When, Where, With, Work, Write, Xml };
			
			private static readonly HashSet<string> IdentifiersInternal = new HashSet<string> { BindVariableIdentifier, DatabaseLinkIdentifier, Identifier, ObjectIdentifier, SchemaIdentifier };

			public static ICollection<string> AllTerminals
			{
				get { return AllTerminalsInternal; }
			}

			public static ICollection<string> Identifiers
			{
				get { return IdentifiersInternal; }
			}

			public static bool IsIdentifier(string terminalId)
			{
				if (!AllTerminalsInternal.Contains(terminalId))
				{
					throw new InvalidOperationException(String.Format("Terminal ID '{0}' not recognized. ", terminalId));
				}

				return IdentifiersInternal.Contains(terminalId);
			}
		}
	}
}
