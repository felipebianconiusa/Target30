export interface PlaidLinkHandler {
  open(): void;
  exit(options?: { force?: boolean }): void;
  destroy(): void;
}

export interface PlaidLinkOnSuccessMetadata {
  institution: { name: string; institution_id: string } | null;
  accounts: Array<{ id: string; name: string; mask: string | null }>;
}

export interface PlaidLinkConfig {
  token: string;
  onSuccess: (publicToken: string, metadata: PlaidLinkOnSuccessMetadata) => void;
  onExit?: (error: unknown, metadata: unknown) => void;
  onEvent?: (eventName: string, metadata: unknown) => void;
}

declare global {
  interface Window {
    Plaid: {
      create(config: PlaidLinkConfig): PlaidLinkHandler;
    };
  }
}
