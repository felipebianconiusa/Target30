import { Component, OnInit, signal } from '@angular/core';
import { ConnectAccount } from '../connect-account/connect-account';
import { PlaidItemSummary, PlaidService } from '../plaid.service';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-accounts',
  imports: [ConnectAccount, RouterLink],
  templateUrl: './accounts.html',
  styleUrl: './accounts.scss',
})
export class Accounts implements OnInit {
  protected readonly items = signal<PlaidItemSummary[]>([]);

  constructor(private readonly plaidService: PlaidService) {}

  ngOnInit(): void {
    this.loadItems();
  }

  protected loadItems(): void {
    this.plaidService.getItems().subscribe({
      next: (items) => this.items.set(items),
    });
  }
}
