// Arrastar campos no editor de layout. O movimento e feito no cliente (suave);
// no "soltar" so a posicao final em PIXELS volta ao Blazor, que converte para
// mm usando o zoom atual (assim o zoom nao precisa falar com o JS).
export function enableDrag(container, dotnet) {
    let drag = null;

    container.addEventListener('pointerdown', e => {
        const field = e.target.closest('.rp-field');
        if (!field || !container.contains(field)) return;
        if (!field.getAttribute('data-name')) return; // campo sem nome nao e ajustavel

        drag = {
            field,
            startX: e.clientX,
            startY: e.clientY,
            origLeft: parseFloat(field.style.left) || 0,
            origTop: parseFloat(field.style.top) || 0
        };
        field.setPointerCapture(e.pointerId);
        field.classList.add('rp-dragging');
        e.preventDefault();
    });

    container.addEventListener('pointermove', e => {
        if (!drag) return;
        const nl = Math.max(0, drag.origLeft + (e.clientX - drag.startX));
        const nt = Math.max(0, drag.origTop + (e.clientY - drag.startY));
        drag.field.style.left = nl + 'px';
        drag.field.style.top = nt + 'px';
    });

    const end = () => {
        if (!drag) return;
        const f = drag.field;
        const name = f.getAttribute('data-name');
        const leftPx = parseFloat(f.style.left) || 0;
        const topPx = parseFloat(f.style.top) || 0;
        f.classList.remove('rp-dragging');
        drag = null;
        dotnet.invokeMethodAsync('OnFieldMoved', name, leftPx, topPx);
    };
    container.addEventListener('pointerup', end);
    container.addEventListener('pointercancel', end);
}
