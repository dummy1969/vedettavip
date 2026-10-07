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

    private bool confirming;

    private void Ask() => confirming = true;

    private void Cancel() => confirming = false;

    private async Task ConfirmAsync()
    {
        confirming = false;
        await OnConfirmed.InvokeAsync();
    }
}
