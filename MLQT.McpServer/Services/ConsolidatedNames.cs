using System.Text.RegularExpressions;

namespace MLQT.McpServer.Services;

/// <summary>
/// The consolidated tool each current-only tool was folded into (B503, the plan's table), and the rewrite
/// that applies it to an answer. The facades call the current tools, whose errors and notes name current
/// tools - "find it with mlqt_search_classes" - and an agent working with the consolidated set would be
/// pointed at a tool that is not there. Applied to every answer while the consolidated set is served;
/// deleted at the cut-over, when the current tools and their messages go.
/// </summary>
internal static partial class ConsolidatedNames
{
    /// <summary>Each current-only tool, and the consolidated tool that does its job.</summary>
    internal static readonly IReadOnlyDictionary<string, string> ReplacedBy = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["mlqt_load_library"] = "mlqt_load",
        ["mlqt_load_repository"] = "mlqt_load",
        ["mlqt_discover_libraries"] = "mlqt_load",
        ["mlqt_server_info"] = "mlqt_session",
        ["mlqt_list_libraries"] = "mlqt_session",
        ["mlqt_list_repositories"] = "mlqt_session",
        ["mlqt_unload_library"] = "mlqt_session",
        ["mlqt_reload"] = "mlqt_session",
        ["mlqt_search_classes"] = "mlqt_find",
        ["mlqt_search_text"] = "mlqt_find",
        ["mlqt_search_by_interface"] = "mlqt_find",
        ["mlqt_list_classes"] = "mlqt_find",
        ["mlqt_get_package_tree"] = "mlqt_browse",
        ["mlqt_get_class_info"] = "mlqt_get_class",
        ["mlqt_get_class_source"] = "mlqt_get_class",
        ["mlqt_get_class_interface"] = "mlqt_get_class",
        ["mlqt_list_class_elements"] = "mlqt_get_class",
        ["mlqt_get_class_documentation"] = "mlqt_get_class",
        ["mlqt_get_class_behavior"] = "mlqt_get_class",
        ["mlqt_list_connections"] = "mlqt_get_class",
        ["mlqt_get_diagram_layout"] = "mlqt_get_class",
        ["mlqt_get_class_resources"] = "mlqt_get_class",
        ["mlqt_create_class"] = "mlqt_write_class",
        ["mlqt_update_class_source"] = "mlqt_write_class",
        ["mlqt_batch_edit"] = "mlqt_edit_class",
        ["mlqt_add_component"] = "mlqt_edit_class",
        ["mlqt_remove_component"] = "mlqt_edit_class",
        ["mlqt_set_component_modifier"] = "mlqt_edit_class",
        ["mlqt_add_extends"] = "mlqt_edit_class",
        ["mlqt_add_import"] = "mlqt_edit_class",
        ["mlqt_add_equation"] = "mlqt_edit_class",
        ["mlqt_add_statement"] = "mlqt_edit_class",
        ["mlqt_add_connection"] = "mlqt_edit_class",
        ["mlqt_remove_connection"] = "mlqt_edit_class",
        ["mlqt_set_component_placement"] = "mlqt_edit_class",
        ["mlqt_set_class_description"] = "mlqt_edit_class",
        ["mlqt_set_component_description"] = "mlqt_edit_class",
        ["mlqt_set_class_documentation"] = "mlqt_edit_class",
        ["mlqt_suppress_rule"] = "mlqt_edit_class",
        ["mlqt_accept_spelling_in_class"] = "mlqt_edit_class",
        ["mlqt_correct_spelling"] = "mlqt_edit_class",
        ["mlqt_rename_class"] = "mlqt_refactor_class",
        ["mlqt_move_class"] = "mlqt_refactor_class",
        ["mlqt_delete_class"] = "mlqt_refactor_class",
        ["mlqt_format_class"] = "mlqt_format",
        ["mlqt_format_code"] = "mlqt_format",
        ["mlqt_check_class"] = "mlqt_check",
        ["mlqt_check_library"] = "mlqt_check",
        ["mlqt_check_style"] = "mlqt_check",
        ["mlqt_spell_check"] = "mlqt_check",
        ["mlqt_spelling_suggestions"] = "mlqt_check",
        ["mlqt_validate_class_references"] = "mlqt_check",
        ["mlqt_get_resource_warnings"] = "mlqt_check",
        ["mlqt_get_style_settings"] = "mlqt_style_settings",
        ["mlqt_set_style_settings"] = "mlqt_style_settings",
        ["mlqt_analyze_dependencies"] = "mlqt_dependencies",
        ["mlqt_get_dependencies"] = "mlqt_dependencies",
        ["mlqt_find_usages"] = "mlqt_dependencies",
        ["mlqt_analyze_impact"] = "mlqt_dependencies",
        ["mlqt_analyze_change_impact"] = "mlqt_dependencies",
        ["mlqt_get_changed_classes"] = "mlqt_dependencies",
        ["mlqt_find_resource_usages"] = "mlqt_dependencies",
    };

    [GeneratedRegex(@"\bmlqt_[a-z_]+", RegexOptions.CultureInvariant)]
    private static partial Regex ToolName();

    // "mlqt_edit_class or mlqt_edit_class", "mlqt_check / mlqt_check" - what two old names become.
    [GeneratedRegex(@"\b(mlqt_[a-z_]+)(?:(?: or | and | / |, )\1\b)+", RegexOptions.CultureInvariant)]
    private static partial Regex Repeated();

    /// <summary><paramref name="text"/> with every current-only tool named as the consolidated one.</summary>
    internal static string Rewrite(string text)
    {
        if (!text.Contains("mlqt_", StringComparison.Ordinal))
            return text;
        var renamed = ToolName().Replace(text, m => ReplacedBy.TryGetValue(m.Value, out var to) ? to : m.Value);
        return Repeated().Replace(renamed, m => m.Groups[1].Value);
    }
}
