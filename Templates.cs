namespace MindMapCanvas;

/// <summary>Built-in starter boards for File > New > From template.</summary>
public static class Templates
{
    public static readonly (string Key, string Name, string Description)[] Catalog =
    {
        ("mindmap", "Mind Map", "A central idea with six branches ready to rename."),
        ("flowchart", "Flowchart", "Start / steps / decision / end, wired top to bottom."),
        ("swot", "SWOT Analysis", "Four color-coded quadrants: strengths, weaknesses, opportunities, threats."),
        ("kanban", "Kanban Board", "To Do / In Progress / Done columns with sample cards."),
        ("orgchart", "Org Chart", "A three-level reporting hierarchy."),
        ("timeline", "Timeline", "Five connected phases running left to right."),
    };

    const double CX = 20000, CY = 20000;

    static NodeModel N(double x, double y, double w, double h, string text, string color,
        string shape = "Rect", string kind = "Shape", double fontSize = 14, bool bold = false,
        double opacity = 1.0, string textColor = "#2D333A", string align = "Center")
        => new()
        {
            X = x, Y = y, W = w, H = h, Text = text, Color = color, Shape = shape,
            Kind = kind, FontSize = fontSize, Bold = bold, Opacity = opacity,
            TextColor = textColor, Align = align
        };

    static ConnectionModel C(NodeModel a, NodeModel b, string fa = null, string ta = null)
        => new() { From = a.Id, To = b.Id, FromAnchor = fa, ToAnchor = ta };

    public static DocumentModel Build(string key)
    {
        var doc = new DocumentModel();
        switch (key)
        {
            case "mindmap": BuildMindMap(doc); break;
            case "flowchart": BuildFlowchart(doc); break;
            case "swot": BuildSwot(doc); break;
            case "kanban": BuildKanban(doc); break;
            case "orgchart": BuildOrgChart(doc); break;
            case "timeline": BuildTimeline(doc); break;
        }
        return doc;
    }

    static void BuildMindMap(DocumentModel d)
    {
        var hub = N(CX - 132, CY - 72, 264, 144, "Central Idea", "#A8D8F0", "Ellipse", fontSize: 18, bold: true);
        d.Nodes.Add(hub);
        var colors = new[] { "#FFF9B1", "#C5E8A5", "#F8A5C2", "#FFCF7D", "#D7B8F3", "#9FE8E0" };
        for (int i = 0; i < 6; i++)
        {
            double ang = i * Math.PI / 3 - Math.PI / 2;
            double x = CX + 430 * Math.Cos(ang) - 90;
            double y = CY + 300 * Math.Sin(ang) - 42;
            var branch = N(x, y, 180, 84, $"Branch {i + 1}", colors[i], bold: true);
            d.Nodes.Add(branch);
            d.Connections.Add(C(hub, branch));
        }
    }

    static void BuildFlowchart(DocumentModel d)
    {
        var start = N(CX - 96, CY - 480, 192, 72, "Start", "#C5E8A5", "Pill", bold: true);
        var s1 = N(CX - 108, CY - 336, 216, 84, "First step", "#FFF9B1");
        var s2 = N(CX - 108, CY - 180, 216, 84, "Second step", "#FFF9B1");
        var dec = N(CX - 132, CY - 24, 264, 132, "Decision?", "#FFCF7D", "Diamond", bold: true);
        var a = N(CX - 384, CY + 192, 192, 84, "Option A", "#A8D8F0");
        var b = N(CX + 192, CY + 192, 192, 84, "Option B", "#F8A5C2");
        var end = N(CX - 96, CY + 384, 192, 72, "End", "#E5A9A9", "Pill", bold: true);
        d.Nodes.AddRange(new[] { start, s1, s2, dec, a, b, end });
        d.Connections.Add(C(start, s1, "Bottom", "Top"));
        d.Connections.Add(C(s1, s2, "Bottom", "Top"));
        d.Connections.Add(C(s2, dec, "Bottom", "Top"));
        d.Connections.Add(C(dec, a, "Left", "Top"));
        d.Connections.Add(C(dec, b, "Right", "Top"));
        d.Connections.Add(C(a, end, "Bottom", "Left"));
        d.Connections.Add(C(b, end, "Bottom", "Right"));
    }

    static void BuildSwot(DocumentModel d)
    {
        var quads = new (string Title, string Color, double X, double Y)[]
        {
            ("Strengths", "#2F9E68", CX - 528, CY - 408),
            ("Weaknesses", "#E5484D", CX + 48, CY - 408),
            ("Opportunities", "#2C7DA0", CX - 528, CY + 24),
            ("Threats", "#F59E0B", CX + 48, CY + 24),
        };
        foreach (var q in quads)
        {
            d.Nodes.Add(N(q.X, q.Y, 480, 384, "", q.Color, kind: "Zone", opacity: 0.25));
            d.Nodes.Add(N(q.X + 24, q.Y + 12, 288, 48, q.Title, "#00FFFFFF", kind: "Text",
                fontSize: 20, bold: true, align: "Left"));
            d.Nodes.Add(N(q.X + 24, q.Y + 72, 336, 60, "Add a point…", "#FFFFFF", fontSize: 13));
        }
    }

    static void BuildKanban(DocumentModel d)
    {
        var cols = new (string Title, string Color, double X)[]
        {
            ("To Do", "#2C7DA0", CX - 600),
            ("In Progress", "#F59E0B", CX - 180),
            ("Done", "#2F9E68", CX + 240),
        };
        foreach (var c in cols)
        {
            d.Nodes.Add(N(c.X, CY - 384, 384, 768, "", c.Color, kind: "Zone", opacity: 0.22));
            d.Nodes.Add(N(c.X + 24, CY - 372, 288, 48, c.Title, "#00FFFFFF", kind: "Text",
                fontSize: 19, bold: true, align: "Left"));
        }
        d.Nodes.Add(N(cols[0].X + 36, CY - 288, 312, 84, "First task", "#FFF9B1"));
        d.Nodes.Add(N(cols[0].X + 36, CY - 180, 312, 84, "Second task", "#FFF9B1"));
        d.Nodes.Add(N(cols[1].X + 36, CY - 288, 312, 84, "Something underway", "#FFCF7D"));
    }

    static void BuildOrgChart(DocumentModel d)
    {
        var ceo = N(CX - 108, CY - 384, 216, 84, "CEO", "#D7B8F3", bold: true, fontSize: 16);
        d.Nodes.Add(ceo);
        var colors = new[] { "#A8D8F0", "#C5E8A5", "#FFCF7D" };
        for (int i = 0; i < 3; i++)
        {
            double mx = CX - 552 + i * 384;
            var mgr = N(mx, CY - 144, 216, 84, $"Manager {i + 1}", colors[i], bold: true);
            d.Nodes.Add(mgr);
            d.Connections.Add(C(ceo, mgr, "Bottom", "Top"));
            for (int j = 0; j < 2; j++)
            {
                var rep = N(mx - 60 + j * 144, CY + 96, 168, 72, "Team member", "#FFFFFF", fontSize: 12);
                d.Nodes.Add(rep);
                d.Connections.Add(C(mgr, rep, "Bottom", "Top"));
            }
        }
    }

    static void BuildTimeline(DocumentModel d)
    {
        var colors = new[] { "#A8D8F0", "#9FE8E0", "#C5E8A5", "#FFF9B1", "#FFCF7D" };
        NodeModel prev = null;
        for (int i = 0; i < 5; i++)
        {
            var phase = N(CX - 936 + i * 408, CY - 48, 264, 96, $"Phase {i + 1}", colors[i], "Pill", bold: true);
            d.Nodes.Add(phase);
            d.Nodes.Add(N(CX - 936 + i * 408, CY + 84, 264, 60,
                "Milestone notes…", "#00FFFFFF", kind: "Text", fontSize: 12));
            if (prev != null) d.Connections.Add(C(prev, phase, "Right", "Left"));
            prev = phase;
        }
    }
}
