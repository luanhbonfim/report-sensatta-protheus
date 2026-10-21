// Arrastar campos no editor de layout. O movimento e feito no cliente (suave);
// so a posicao final (em mm) e enviada ao Blazor no "soltar".
export function enableDrag(container, dotnet, scale) {
    let drag = null;

    container.addEventListener('pointerdown', e => {
        const field = e.target.closest('.rp-field');
        if (!field || !container.contains(field)) return;
        const name = field.getAttribute('data-name');
        if (!name) return; // campo sem nome nao e ajustavel

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
        let nl = Math.max(0, drag.origLeft + (e.clientX - drag.startX));
        let nt = Math.max(0, drag.origTop + (e.clientY - drag.startY));
        drag.field.style.left = nl + 'px';
        drag.field.style.top = nt + 'px';
    });

    const end = () => {
        if (!drag) return;
        const f = drag.field;
        const name = f.getAttribute('data-name');
        const leftMm = (parseFloat(f.style.left) || 0) / scale;
        const topMm = (parseFloat(f.style.top) || 0) / scale;
        f.classList.remove('rp-dragging');
        drag = null;
        dotnet.invokeMethodAsync('OnFieldMoved', name, leftMm, topMm);
    };
    container.addEventListener('pointerup', end);
    container.addEventListener('pointercancel', end);
}
