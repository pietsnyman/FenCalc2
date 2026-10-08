namespace FenCalc2.ViewModels;

/// <summary>One line of the wall window's Checks list (top-level so the XAML
/// compiled-binding x:DataType can name it: <c>vm:WallCheckRow</c>).</summary>
public sealed record WallCheckRow(string Name, string Verdict, string Detail);
