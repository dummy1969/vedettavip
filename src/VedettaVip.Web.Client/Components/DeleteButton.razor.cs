// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;

namespace VedettaVip.Web.Client.Components;

/// <summary>Bottone di eliminazione con conferma al secondo clic.</summary>
public partial class DeleteButton
{
    [Parameter] public bool Busy { get; set; }
    [Parameter] public EventCallback OnConfirmed { get; set; }
    [Parameter] public string Text { get; set; } = "Elimina";
    [Parameter] public string ConfirmText { get; set; } = "Conferma eliminazione";
    /// <summary>Solo il cestino (Text nel tooltip); la conferma resta a parole.</summary>
    [Parameter] public bool IconOnly { get; set; }

    private bool confirming;

    private void Ask() => confirming = true;

    private void Cancel() => confirming = false;

    private async Task ConfirmAsync()
    {
        confirming = false;
        await OnConfirmed.InvokeAsync();
    }
}
