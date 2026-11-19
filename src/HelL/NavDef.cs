using System;
using HelL.Pages;

namespace HelL;

public sealed record NavDef(string Id, string Title, string Glyph, Func<PageBase> Create);
