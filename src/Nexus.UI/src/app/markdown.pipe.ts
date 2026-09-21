import { Pipe, PipeTransform } from '@angular/core'
import { marked } from 'marked'
import { normalizeMarkdown } from './utils'

@Pipe({
  name: 'markdown',
  standalone: true,
})
export class MarkdownPipe implements PipeTransform {
  transform(value: string | null | undefined): string {
    return marked.parse(normalizeMarkdown(value ?? ''), { async: false, gfm: true }) as string
  }
}
