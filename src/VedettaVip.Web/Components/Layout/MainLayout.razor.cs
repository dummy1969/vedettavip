// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace VedettaVip.Web.Components.Layout;

public partial class MainLayout
{
    [Inject] private IOptions<AppOptions> App { get; set; } = default!;

    private string SourceUrl => App.Value.EffectiveSourceUrl;
}
