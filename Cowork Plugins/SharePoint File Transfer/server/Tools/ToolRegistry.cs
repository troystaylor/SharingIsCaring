using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace SharePointTransferMcp.Tools;

public sealed class ToolRegistry
{
	private readonly ConcurrentDictionary<string, ToolDescriptor> _tools = new ConcurrentDictionary<string, ToolDescriptor>(StringComparer.Ordinal);

	public IReadOnlyCollection<ToolDescriptor> Tools => _tools.Values.OrderBy((ToolDescriptor t) => t.Name).ToList();

	public void Add(ToolDescriptor tool)
	{
		if (!_tools.TryAdd(tool.Name, tool))
		{
			throw new InvalidOperationException("duplicate tool: " + tool.Name);
		}
	}

	public bool TryGet(string name, [NotNullWhen(true)] out ToolDescriptor? tool)
	{
		return _tools.TryGetValue(name, out tool);
	}

	public void RegisterAll(IServiceProvider _)
	{
		Add(ListSitesTool.Build());
		Add(ListDrivesTool.Build());
		Add(ListFolderTool.Build());
		Add(GetItemTool.Build());
		Add(UploadFromUrlTool.Build());
		Add(ResumeUploadFromUrlTool.Build());
		Add(StartUploadSessionTool.Build());
		Add(GetUploadStatusTool.Build());
		Add(CancelUploadTool.Build());
		Add(CreateFolderTool.Build());
		Add(MoveItemTool.Build());
		Add(SetMetadataTool.Build());
		Add(CreateLinkTool.Build());
	}
}
