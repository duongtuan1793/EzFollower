using System.Drawing;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Helpers;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using Newtonsoft.Json;

namespace EzFollower;

public class EzFollowerSettings : ISettings
{
    //Mandatory setting to allow enabling/disabling your plugin
    public ToggleNode Enable { get; set; } = new ToggleNode(false);

    //Put all your settings here if you can.
    //There's a bunch of ready-made setting nodes,
    //nested menu support and even custom callbacks are supported.
    //If you want to override DrawSettings instead, you better have a very good reason.
    public ToggleNode Run { get; set; } = new ToggleNode(false);
    public HotkeyNode StartKey { get; set; } = new HotkeyNode(Keys.Insert);
    public HotkeyNode MoveKey { get; set; } = new HotkeyNode(Keys.T);
    public HotkeyNode QuickLootKey { get; set; } = new HotkeyNode(Keys.F9);
    public TextNode LeaderName { get; set; } = new TextNode("CoU_CoBeKieuKy");
    public ToggleNode EnableLoot { get; set; } = new ToggleNode(true);
    public ToggleNode EnableAutoRevive { get; set; } = new ToggleNode(true);
    public ToggleNode EnablePortalFollow { get; set; } = new ToggleNode(true);
    public ToggleNode EnableFlask { get; set; } = new ToggleNode(true);
    public ToggleNode DrawWorldPath { get; set; } = new ToggleNode(false);

    public RangeNode<int> FlaskThreshold { get; set; } = new RangeNode<int>(95, 0, 100);
    public RangeNode<int> FlaskDelay { get; set; } = new RangeNode<int>(1000, 100, 10000);
    public RangeNode<int> FindPathDelay { get; set; } = new RangeNode<int>(360, 200, 2000);
    public RangeNode<int> MoveRange { get; set; } = new RangeNode<int>(290, 200, 500);
    public RangeNode<int> CircleRadius { get; set; } = new RangeNode<int>(0, 0, 200);
    public RangeNode<int> StartFollowRange { get; set; } = new RangeNode<int>(60, 10, 200);
    public RangeNode<int> StopFollowRange { get; set; } = new RangeNode<int>(30, 10, 200);
    public RangeNode<int> PortalRemain { get; set; } = new RangeNode<int>(5, 1, 5);
    public RangeNode<int> PortalDelay { get; set; } = new RangeNode<int>(3, 1, 10);
}
