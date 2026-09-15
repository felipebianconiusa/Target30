import {
  Component,
  ElementRef,
  EventEmitter,
  HostListener,
  Input,
  Output,
  signal,
} from '@angular/core';

export interface MultiSelectOption {
  value: string;
  label: string;
}

@Component({
  selector: 'app-multi-select',
  imports: [],
  templateUrl: './multi-select.html',
  styleUrl: './multi-select.scss',
})
export class MultiSelect {
  @Input({ required: true }) allLabel = 'Todos';
  @Input({ required: true }) options: MultiSelectOption[] = [];
  @Input() selected: string[] = [];
  @Output() selectedChange = new EventEmitter<string[]>();

  protected readonly open = signal(false);

  // Método normal (não computed): `selected`/`options` são @Input comuns, não signals,
  // então precisam ser reavaliados a cada ciclo de change detection, não só na 1ª leitura.
  protected buttonLabel(): string {
    if (this.selected.length === 0) return this.allLabel;
    if (this.selected.length === 1) {
      return this.options.find((o) => o.value === this.selected[0])?.label ?? this.selected[0];
    }
    return `${this.selected.length} selecionadas`;
  }

  constructor(private readonly elementRef: ElementRef<HTMLElement>) {}

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }

  protected toggleOpen(): void {
    this.open.update((v) => !v);
  }

  protected isChecked(value: string): boolean {
    return this.selected.includes(value);
  }

  protected toggle(value: string): void {
    const next = this.isChecked(value)
      ? this.selected.filter((v) => v !== value)
      : [...this.selected, value];
    this.selectedChange.emit(next);
  }

  protected clear(): void {
    this.selectedChange.emit([]);
  }
}
