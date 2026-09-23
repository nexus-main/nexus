import { Component, computed, input, output } from '@angular/core'
import { TreeNode } from 'primeng/api'
import { Tree, TreeModule } from 'primeng/tree'
import { TooltipModule } from 'primeng/tooltip'
import { CatalogNode } from '../nexus.service'
import { abbreviateMiddle, lastSegment } from '../utils'

@Component({
  selector: 'app-catalog-tree',
  standalone: true,
  imports: [TreeModule, TooltipModule],
  host: { class: 'block min-w-0' },
  template: `
    <p-tree
      #tree
      [value]="treeNodes()"
      [trackBy]="trackNode"
      selectionMode="single"
      [selection]="selectedTreeNode()"
      [metaKeySelection]="true"
      [filter]="false"
      ariaLabel="Catalogs"
      emptyMessage="No catalogs match your search."
      styleClass="w-full min-w-0 bg-transparent p-0 [&_.p-treenode]:py-0 [&_.p-tree-empty-message]:px-4 [&_.p-tree-empty-message]:py-8 [&_.p-tree-empty-message]:text-center [&_.p-tree-empty-message]:text-sm [&_.p-tree-empty-message]:leading-6 [&_.p-tree-empty-message]:text-[var(--p-text-muted-color)] [&_.p-tree-node-content]:min-w-0 [&_.p-tree-node-content]:rounded-sm [&_.p-tree-node-content]:border-l-2 [&_.p-tree-node-content]:border-l-transparent [&_.p-tree-node-content]:py-0.5 [&_.p-tree-node-content]:transition-colors [&_.p-tree-node-content.p-tree-node-selected]:border-l-[var(--p-primary-color)] [&_.p-tree-node-label]:min-w-0 [&_.p-tree-node-label]:flex-1 [&_.p-tree-node-toggle-button]:h-4 [&_.p-tree-node-toggle-button]:w-4 [&_.p-tree-node-leaf_.p-tree-node-toggle-button]:invisible [&_.p-tree-node-toggle-icon]:h-2 [&_.p-tree-node-toggle-icon]:w-2 [&_.p-tree-node-toggle-icon]:opacity-70"
      (onNodeSelect)="activateNode($event.node, tree)"
      (onNodeUnselect)="activateNode($event.node, tree)"
      (onNodeExpand)="toggle.emit($event.node.data!)"
      (onNodeCollapse)="toggle.emit($event.node.data!)"
    >
      <ng-template pTemplate="default" let-treeNode>
        @let node = treeNode.data;
        <div class="flex min-w-0 items-center gap-2" [pTooltip]="node.title" [tooltipDisabled]="!node.title" [showDelay]="1000">
          <span class="shrink-0 truncate font-mono text-[13px] font-semibold leading-5 text-[var(--p-text-color)]">
            <span class="sm:hidden">{{ abbreviateMiddle(treeNode.label, 28) }}</span>
            <span class="hidden sm:inline">{{ treeNode.label }}</span>
          </span>
          @if (!node.isFake && node.title) {
            <span class="shrink-0 text-[10px] text-[var(--p-text-muted-color)]" aria-hidden="true">·</span>
            <span class="min-w-0 truncate text-xs text-[var(--p-text-muted-color)] opacity-90">{{ node.title }}</span>
          }
        </div>
      </ng-template>
    </p-tree>
  `,
})
export class CatalogTreeComponent {
  readonly nodes = input.required<CatalogNode[]>()
  readonly selectedNodeKey = input.required<string>()
  readonly expandedNodeKeys = input.required<ReadonlySet<string>>()
  readonly expandableNodeKeys = input.required<ReadonlySet<string>>()
  readonly activate = output<CatalogNode>()
  readonly toggle = output<CatalogNode>()
  readonly abbreviateMiddle = abbreviateMiddle
  readonly trackNode = (_index: number, node: TreeNode<CatalogNode>) => node.key

  readonly treeNodes = computed<TreeNode<CatalogNode>[]>(() => {
    const roots: TreeNode<CatalogNode>[] = []
    const stack: TreeNode<CatalogNode>[] = []
    const expanded = this.expandedNodeKeys()
    const expandable = this.expandableNodeKeys()

    for (const node of this.nodes()) {
      while (stack.length && stack[stack.length - 1].data!.depth >= node.depth) stack.pop()

      const treeNode: TreeNode<CatalogNode> = {
        key: node.nodeKey,
        label: lastSegment(node.id),
        data: node,
        expanded: expanded.has(node.nodeKey),
        leaf: !expandable.has(node.nodeKey),
        children: [],
      }
      const parent = stack[stack.length - 1]
      if (parent) parent.children!.push(treeNode)
      else roots.push(treeNode)
      stack.push(treeNode)
    }

    return roots
  })

  readonly selectedTreeNode = computed(() => {
    const key = this.selectedNodeKey()
    const stack = [...this.treeNodes()]
    while (stack.length) {
      const node = stack.pop()!
      if (node.key === key) return node
      stack.push(...(node.children ?? []))
    }
    return null
  })

  activateNode(node: TreeNode<CatalogNode>, tree: Tree) {
    this.activate.emit(node.data!)
    // Activation may only toggle a synthetic folder; selection stays parent-owned.
    tree.selection.set(this.selectedTreeNode())
  }
}
