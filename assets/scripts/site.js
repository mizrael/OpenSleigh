window.onload = function () {
    const findOffset = (element) => {
        var top = 0, left = 0;

        do {
            top += element.offsetTop || 0;
            left += element.offsetLeft || 0;
            element = element.offsetParent;
        } while (element);

        return {
            top: top,
            left: left
        };
    };

    const body = document.body; 
        headerOffset = findOffset(body);

    const handleHeaderPos = () => {
        // body.scrollTop is deprecated and no longer available on Firefox
        const bodyScrollTop = document.documentElement.scrollTop || document.body.scrollTop;

        //if scroll position is greater than stickyDiv, make div fixed at top 
        // by adding class 'fixed'
        if (bodyScrollTop > headerOffset.top) {
            body.classList.add('header-fixed');
        } else {
            body.classList.remove('header-fixed');
        }
    };

    window.onscroll = () => {
        handleHeaderPos();
    };
    handleHeaderPos();
};