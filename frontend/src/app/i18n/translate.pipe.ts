import { Pipe, PipeTransform } from '@angular/core';
import { TranslationService } from './translation.service';

// impure: precisa reavaliar quando o idioma muda em outro lugar (o texto de
// entrada, a chave, não muda — só o resultado)
@Pipe({ name: 'translate', pure: false })
export class TranslatePipe implements PipeTransform {
  constructor(private readonly translationService: TranslationService) {}

  transform(key: string, params?: Record<string, string | number>): string {
    return this.translationService.t(key, params);
  }
}
