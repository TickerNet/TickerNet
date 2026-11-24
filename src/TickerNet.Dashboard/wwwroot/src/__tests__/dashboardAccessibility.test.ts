import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { NodeTypes, type ElementNode } from '@vue/compiler-dom';
import { parse } from '@vue/compiler-sfc';
import { describe, expect, it } from 'vitest';

const srcRoot = join(dirname(fileURLToPath(import.meta.url)), '..');

function vueFiles(dir: string): string[] {
    return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
        const fullPath = join(dir, entry.name);
        if (entry.isDirectory()) return vueFiles(fullPath);
        return entry.name.endsWith('.vue') ? [fullPath] : [];
    });
}

function parseTemplate(file: string): ElementNode[] {
    const source = readFileSync(file, 'utf8');
    const { descriptor, errors } = parse(source, { filename: file });
    expect(errors.map(String)).toEqual([]);

    const elements: ElementNode[] = [];
    // v-if / v-for wrap children in branches, so walk every node container.
    const walk = (node: unknown): void => {
        if (!node || typeof node !== 'object') return;
        const current = node as {
            type?: number;
            children?: unknown[];
            branches?: unknown[];
        };
        if (current.type === NodeTypes.ELEMENT) elements.push(node as ElementNode);
        for (const child of current.children ?? []) walk(child);
        for (const branch of current.branches ?? []) walk(branch);
    };
    walk(descriptor.template?.ast);
    return elements;
}

function attributes(element: ElementNode): Map<string, string> {
    const result = new Map<string, string>();
    for (const prop of element.props) {
        if (prop.type === NodeTypes.ATTRIBUTE) {
            result.set(prop.name, prop.value?.content ?? '');
        } else if (
            prop.type === NodeTypes.DIRECTIVE
            && prop.name === 'bind'
            && prop.arg?.type === NodeTypes.SIMPLE_EXPRESSION
        ) {
            const expression = prop.exp?.type === NodeTypes.SIMPLE_EXPRESSION ? prop.exp.content : '';
            result.set(prop.arg.content, expression);
        }
    }
    return result;
}

function childText(node: unknown): string {
    if (!node || typeof node !== 'object') return '';
    const current = node as {
        type?: number;
        tag?: string;
        content?: string | { content?: string };
        children?: unknown[];
        branches?: unknown[];
    };
    // Icons are not accessible names, and tooltips are not button content.
    if (current.tag === 'v-icon' || current.tag === 'v-tooltip') return '';
    if (current.type === NodeTypes.TEXT || current.type === NodeTypes.INTERPOLATION) {
        return typeof current.content === 'string' ? current.content : current.content?.content ?? '';
    }
    return [
        ...(current.children ?? []).map(childText),
        ...(current.branches ?? []).map(childText),
    ].join('');
}

function unlabeledIconButtons(): string[] {
    const missing: string[] = [];

    for (const file of vueFiles(srcRoot)) {
        const elements = parseTemplate(file);
        for (const element of elements) {
            // `v-btn-group` and `v-btn-toggle` are not buttons.
            if (element.tag !== 'v-btn' && element.tag !== 'button') continue;
            const attrs = attributes(element);
            const hasIcon = attrs.has('icon')
                || element.children.some(child => child.type === NodeTypes.ELEMENT && child.tag === 'v-icon');
            const hasVisibleText = element.children.map(childText).join('').trim().length > 0;

            if (hasIcon && !attrs.has('text') && !attrs.has('aria-label') && !hasVisibleText) {
                const path = relative(srcRoot, file).replace(/\\/g, '/');
                missing.push(`${path}:${element.loc.start.line}`);
            }
        }
    }

    return missing;
}

describe('dashboard accessibility', () => {
    it('gives every icon-only button an accessible name', () => {
        expect(unlabeledIconButtons().join('\n')).toBe('');
    });

    it('names the time zone button when its text is hidden on small screens', () => {
        const elements = parseTemplate(join(srcRoot, 'components', 'layout', 'DashboardLayout.vue'));
        const timeZoneButton = elements.find(
            element => element.tag === 'v-btn' && attributes(element).get('prepend-icon') === 'mdi-earth',
        );

        expect(timeZoneButton).toBeDefined();
        expect(attributes(timeZoneButton!).get('aria-label')).toContain('Display time zone');
    });

    it('makes the header branding a real link', () => {
        const elements = parseTemplate(join(srcRoot, 'components', 'layout', 'DashboardLayout.vue'));
        const homeLink = elements.find(
            element => element.tag === 'router-link' && attributes(element).get('to') === '/',
        );

        expect(homeLink).toBeDefined();
        expect(attributes(homeLink!).get('aria-label')).toBe('TickerNet dashboard home');
    });
});
