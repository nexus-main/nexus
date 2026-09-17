import { Component, computed, input, output } from '@angular/core'
import { TreeNode } from 'primeng/api'
import { Tree, TreeModule } from 'primeng/tree'
import { CatalogNode } from '../nexus.service'
import { abbreviateMiddle, lastSegment } from '../utils'

@Component({
  selector: 'app-catalog-tree',
  standalone: true,
  imports: [TreeModule],
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
      styleClass="w-full min-w-0 bg-transparent p-0 [&_.p-tree-node-content]:min-w-0 [&_.p-tree-node-label]:min-w-0 [&_.p-tree-node-label]:flex-1"
      (onNodeSelect)="activateNode($event.node, tree)"
      (onNodeUnselect)="activateNode($event.node, tree)"
      (onNodeExpand)="toggle.emit($event.node.data!)"
      (onNodeCollapse)="toggle.emit($event.node.data!)"
    >
      <ng-template pTemplate="default" let-treeNode>
        @let node = treeNode.data;
        <div class="min-w-0" [title]="node.id ?? '/'">
          <div class="flex min-w-0 items-center">
            <span class="truncate font-mono text-[13px] leading-5 text-slate-100">
              <span class="sm:hidden">{{ abbreviateMiddle(treeNode.label, 28) }}</span>
              <span class="hidden sm:inline">{{ treeNode.label }}</span>
            </span>
          </div>
          @if (!node.isFake && node.title) {
            <div class="mt-1 flex min-w-0 items-center gap-2 text-xs text-slate-500">
              <span class="truncate">{{ node.title }}</span>
            </div>
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
